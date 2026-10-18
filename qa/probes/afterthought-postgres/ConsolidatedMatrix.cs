using System.Data.Common;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WovenBackend.Data;
using WovenBackend.Data.Entities;
using WovenBackend.data.Entities.Moments;
using WovenBackend.Endpoints;
using WovenBackend.Services;
using WovenBackend.Services.Moments;
using WovenBackend.Services.Security;
using WovenBackend.Services.Trust;
using Npgsql;
using Pgvector.EntityFrameworkCore;

internal static class ConsolidatedMatrix
{
    public static async Task ExecuteAsync(string connection, Func<string, Func<Task>, Task> run,
        Func<Task<MatchAfterthought>> seed, Func<MatchAfterthought, int, bool, Task> verify)
    {
        AtomicContext Db(IInterceptor? interceptor = null)
        {
            var options = new DbContextOptionsBuilder<WovenDbContext>().UseNpgsql(connection, o => o.EnableRetryOnFailure());
            if (interceptor != null) options.AddInterceptors(interceptor);
            return new(options.Options);
        }
        AfterthoughtIntegrationService Integration(WovenDbContext db, IMatchSignalService? signals = null, ITrustService? trust = null)
            => new(db, signals ?? new MatchSignalService(db, NullLogger<MatchSignalService>.Instance), trust ?? DispatchProxy.Create<ITrustService, CountingTrust>(), NullLogger<AfterthoughtIntegrationService>.Instance);
        AfterthoughtService Service(WovenDbContext db, IMatchSignalService? signals = null, ITrustService? trust = null)
            => new(db, Integration(db, signals, trust), NullLogger<AfterthoughtService>.Instance);
        async Task<MatchAfterthought> Blank()
        {
            var row = await seed(); await using var db = Db();
            await db.MatchAfterthoughts.Where(a => a.Id == row.Id).ExecuteDeleteAsync(); return row;
        }
        async Task Check(bool good, string message) { if (!good) throw new InvalidOperationException(message); await Task.CompletedTask; }

        foreach (var path in new[] { "trial_end", "unmatch", "expiry", "block" })
            await run("path_" + path + "_submission_routes_to_expected_destination", async () =>
            {
                var row = await Blank(); await using var db = Db(); var trust = DispatchProxy.Create<ITrustService, CountingTrust>();
                await Service(db, trust: trust).RecordAsync(row.MatchId, row.FromUserId, row.AboutUserId, path,
                    path is "expiry" or "block" ? null : (short)3, "synthetic", null, path == "block");
                var saved = await db.MatchAfterthoughts.AsNoTracking().SingleAsync(a => a.MatchId == row.MatchId);
                await Check(saved.ClosePath == path && saved.ProcessedAt != null, "Wrong path/completion");
                await Check(await db.MatchSignalLogs.CountAsync(s => s.ViewerId == row.FromUserId) == (path is "trial_end" or "unmatch" ? 1 : 0), "Wrong matching destination");
                await Check(((CountingTrust)(object)trust).Calls == (path == "block" ? 1 : 0), "Wrong trust destination");
            });
        foreach (var stars in new short[] { 1, 2, 3, 4, 5 })
            await run("normalization_" + stars + "_stars", async () =>
            {
                var row = await seed(); await using var db = Db();
                var saved = await db.MatchAfterthoughts.SingleAsync(a => a.Id == row.Id); saved.Rating = stars; await db.SaveChangesAsync();
                await Integration(db).ProcessAfterthoughtAsync(row.Id);
                var signal = await db.MatchSignalLogs.SingleAsync(s => s.ViewerId == row.FromUserId);
                await Check(Math.Abs(signal.EventValue - (stars - 1) / 4f) < .001, "Wrong normalization");
            });
        var invalid = new (string Path, short? Stars, bool Safety, string? Tag, string? Note)[] {
            ("trial_end",0,false,"tag",null), ("unmatch",6,false,"tag",null),
            ("expiry",3,false,"tag",null), ("block",3,true,"tag",null),
            ("block",null,false,"tag",null), ("invalid",null,false,"tag",null),
            ("unmatch",3,false,new string('x',51),null), ("unmatch",3,false,"tag",new string('x',301)) };
        for (var i = 0; i < invalid.Length; i++)
        {
            var input = invalid[i]; await run("invalid_submission_" + i + "_persists_nothing", async () =>
            {
                var row = await Blank(); await using var db = Db(); var rejected = false;
                try { await Service(db).RecordAsync(row.MatchId,row.FromUserId,row.AboutUserId,input.Path,input.Stars,input.Tag,input.Note,input.Safety); }
                catch (ArgumentException) { rejected = true; }
                await Check(rejected && !await db.MatchAfterthoughts.AnyAsync(a => a.MatchId == row.MatchId), "Invalid input saved or not rejected");
            });
        }
        foreach (var path in new[] { "block", "unmatch" })
        foreach (var initial in new[] { .8f, .1f })
            await run("real_trust_" + path + "_initial_" + initial, async () =>
            {
                var row = await seed(); await using var db = Db();
                db.Users.AddRange(new User { Id=row.FromUserId, Email="actor@qa.invalid",EmailHash="actor"+row.Id,TrustScore=.8f },
                    new User { Id=row.AboutUserId,Email="target@qa.invalid",EmailHash="target"+row.Id,TrustScore=initial });
                var saved=await db.MatchAfterthoughts.SingleAsync(a=>a.Id==row.Id);
                saved.IsSafety=true; saved.Rating=null; saved.ClosePath=path; await db.SaveChangesAsync();
                var audit=DispatchProxy.Create<ISecurityAuditService, AuditCounter>();
                var trust=new TrustService(db,DispatchProxy.Create<ICacheService,UnexpectedCallStub>(),audit,NullLogger<TrustService>.Instance);
                await Integration(db,trust:trust).ProcessAfterthoughtAsync(row.Id);
                await using var fresh=Db(); var target=await fresh.Users.FindAsync(row.AboutUserId); var actor=await fresh.Users.FindAsync(row.FromUserId);
                await Check(Math.Abs(target!.TrustScore-Math.Min(initial,path=="block"?.15f:.30f))<.001 && actor!.TrustScore==.8f,"Incorrect real trust cap/actor");
                await verify(row,0,true);
            });
        await run("signal_saved_then_exception_fresh_retry_does_not_duplicate",async()=>
        {
            var row=await seed(); await using(var db=Db())
            {
                try { await Integration(db,new SavedThenFailure(new MatchSignalService(db,NullLogger<MatchSignalService>.Instance))).ProcessAfterthoughtAsync(row.Id); }
                catch(InvalidOperationException ex) when(ex.Message=="Injected failure after signal save") { }
            }
            await verify(row,1,false); await using(var retry=Db()) await Integration(retry).ProcessAfterthoughtAsync(row.Id); await verify(row,1,true);
        });
        await run("cancel_during_signal_then_fresh_processor_recovers",async()=>
        {
            var row=await seed(); using var cancellation=new CancellationTokenSource(); await using(var db=Db())
            {
                try { await Integration(db,new CancelSignals(cancellation)).ProcessAfterthoughtAsync(row.Id,cancellation.Token); }
                catch(OperationCanceledException) { }
            }
            await using(var retry=Db()) await Integration(retry).ProcessAfterthoughtAsync(row.Id); await verify(row,1,true);
        });
        await run("cancel_before_claim_leaves_pending_then_retry_recovers",async()=>
        {
            var row=await seed(); using var cancellation=new CancellationTokenSource(); cancellation.Cancel(); await using(var db=Db())
            { try { await Integration(db).ProcessAfterthoughtAsync(row.Id,cancellation.Token); } catch(OperationCanceledException) { } }
            await verify(row,0,false); await using(var retry=Db()) await Integration(retry).ProcessAfterthoughtAsync(row.Id); await verify(row,1,true);
        });
        await run("load_failure_after_claim_fresh_processor_recovers",async()=>
        {
            var row=await seed(); await using(var db=Db(new FailClaimLoad()))
            { try { await Integration(db).ProcessAfterthoughtAsync(row.Id); } catch(InvalidOperationException ex) when(ex.Message=="Injected claim-load failure") { } }
            await using(var retry=Db()) await Integration(retry).ProcessAfterthoughtAsync(row.Id); await verify(row,1,true);
        });
        await run("safety_with_historical_stars_never_enters_matching",async()=>
        {
            var row=await seed(); await using var db=Db(); var saved=await db.MatchAfterthoughts.SingleAsync(a=>a.Id==row.Id); saved.IsSafety=true; await db.SaveChangesAsync();
            await Integration(db).ProcessAfterthoughtAsync(row.Id); await verify(row,0,true);
        });
        foreach(var path in new[]{"trial_end","unmatch","expiry","block"})
            await run("completed_"+path+"_submission_rejects_duplicate",async()=>
            {
                var row=await Blank(); await using(var db=Db()) await Service(db).RecordAsync(row.MatchId,row.FromUserId,row.AboutUserId,path,path is "expiry" or "block"?null:(short)3,"tag",null,path=="block");
                await using var retry=Db(); var rejected=false;
                try { await Service(retry).RecordAsync(row.MatchId,row.FromUserId,row.AboutUserId,path,path is "expiry" or "block"?null:(short)3,"tag",null,path=="block"); }
                catch(InvalidOperationException ex) when(ex.Message=="Afterthought already submitted for this match") { rejected=true; }
                await Check(rejected,"Completed duplicate accepted");
            });
        foreach(var mode in new[]{"outsider","open","missing","blocked","expiry","unmatch","block"})
            await run("deferred_endpoint_"+mode,async()=>
            {
                var row=await Blank(); await using var db=Db(); var match=await db.Matches.SingleAsync(m=>m.Id==row.MatchId);
                match.ClosedReason=mode is "blocked" or "block"?ClosedReason.BLOCK:mode=="expiry"?ClosedReason.EXPIRE:ClosedReason.UNMATCH;
                if(mode=="open") match.BalloonState=BalloonState.ACTIVE;
                if(mode=="blocked") db.Blocks.Add(new Block{BlockerId=row.AboutUserId,BlockedId=row.FromUserId});
                await db.SaveChangesAsync();
                var expected=mode=="outsider"||mode=="blocked"?403:mode=="open"?400:mode=="missing"?404:200;
                var response=await Endpoint(db,Service(db),mode=="missing"?Guid.NewGuid():row.MatchId,mode=="outsider"?row.FromUserId+100000:row.FromUserId,
                    new {rating=mode is "expiry" or "block" or "blocked"?(int?)null:3,reasonTag="tag",noteText=(string?)null});
                await Check(response==expected,"Expected endpoint "+expected+", actual "+response);
                await Check(await db.MatchAfterthoughts.CountAsync(a=>a.MatchId==row.MatchId)==(expected==200?1:0),"Endpoint persisted unexpected feedback");
            });
        await run("opposite_participants_feedback_creates_distinct_directional_signals",async()=>
        {
            var row=await Blank(); await using var db=Db();
            await Service(db).RecordAsync(row.MatchId,row.FromUserId,row.AboutUserId,"unmatch",1,"tag",null,false);
            await Service(db).RecordAsync(row.MatchId,row.AboutUserId,row.FromUserId,"unmatch",5,"tag",null,false);
            await Check(await db.MatchAfterthoughts.CountAsync(a=>a.MatchId==row.MatchId)==2,"Missing participant feedback");
            await Check(await db.MatchSignalLogs.CountAsync(s=>(s.ViewerId==row.FromUserId&&s.CandidateId==row.AboutUserId)||(s.ViewerId==row.AboutUserId&&s.CandidateId==row.FromUserId))==2,"Missing directional signals");
        });
        await run("fresh_local_full_application_migration_chain",async()=>
        {
            const string migrationDb="woven_qa_migrations_consolidated_20261016";
            await using(var admin=new NpgsqlConnection(connection))
            {
                await admin.OpenAsync();
                await using var exists=new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM pg_database WHERE datname=@name)",admin);exists.Parameters.AddWithValue("name",migrationDb);
                if(!(bool)(await exists.ExecuteScalarAsync())!)
                {await using var create=new NpgsqlCommand("CREATE DATABASE \"woven_qa_migrations_consolidated_20261016\"",admin);await create.ExecuteNonQueryAsync();}
            }
            var parsed=new NpgsqlConnectionStringBuilder(connection){Database=migrationDb};
            if(parsed.Host!="127.0.0.1"||parsed.Port!=55439||parsed.Username!="woven_qa")throw new InvalidOperationException("Refusing migration target");
            await using(var identity=new NpgsqlConnection(parsed.ConnectionString))
            {await identity.OpenAsync();await using var command=new NpgsqlCommand("SELECT current_database()",identity);if((string)(await command.ExecuteScalarAsync())! !=migrationDb)throw new InvalidOperationException("Wrong effective migration database");}
            var options=new DbContextOptionsBuilder<WovenDbContext>().UseNpgsql(parsed.ConnectionString,o=>o.UseVector()).Options;
            await using var full=new WovenDbContext(options);
            await full.Database.MigrateAsync();
        });
    }

    private static async Task<int> Endpoint(WovenDbContext db,IAfterthoughtService service,Guid id,int actor,object payload)
    {
        var builder=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Testing"});builder.Logging.ClearProviders();
        foreach(var type in typeof(WovenDbContext).Assembly.GetTypes().Where(t=>t.IsInterface&&t.Namespace?.StartsWith("WovenBackend.Services")==true))
            builder.Services.AddSingleton(type,_=>DispatchProxy.Create(type,typeof(UnexpectedCallStub)));
        builder.Services.AddSingleton(db);builder.Services.AddSingleton<IAfterthoughtService>(service);builder.Services.AddAuthorization();
        builder.Services.AddSingleton<IAuthenticationService>(new ForbidAuthentication());
        await using var app=builder.Build();app.MapMatchesEndpoints();
        var endpoint=((IEndpointRouteBuilder)app).DataSources.SelectMany(s=>s.Endpoints).OfType<RouteEndpoint>().Single(e=>e.RoutePattern.RawText=="/matches/{matchId:guid}/afterthought");
        var http=new DefaultHttpContext{RequestServices=app.Services,User=new ClaimsPrincipal(new ClaimsIdentity([new Claim("uid",actor.ToString())],"Synthetic"))};
        http.Request.Method="POST";http.Request.RouteValues["matchId"]=id.ToString();var bytes=JsonSerializer.SerializeToUtf8Bytes(payload);
        http.Request.Body=new MemoryStream(bytes);http.Request.ContentType="application/json";http.Request.ContentLength=bytes.Length;http.Features.Set<IHttpRequestBodyDetectionFeature>(new BodyAvailable());http.Response.Body=new MemoryStream();
        await endpoint.RequestDelegate!(http);return http.Response.StatusCode;
    }
}
public class AuditCounter:DispatchProxy
{
    public int Calls{get;private set;}
    protected override object? Invoke(MethodInfo? method,object?[]? args){Calls++;return null;}
}
internal sealed class SavedThenFailure(IMatchSignalService inner):IMatchSignalService
{
    public async Task RecordAsync(int viewerId,int candidateId,string eventType,float eventValue,string? metadataJson=null,CancellationToken ct=default)
    {await inner.RecordAsync(viewerId,candidateId,eventType,eventValue,metadataJson,ct);throw new InvalidOperationException("Injected failure after signal save");}
}
internal sealed class CancelSignals(CancellationTokenSource cancellation):IMatchSignalService
{
    public Task RecordAsync(int viewerId,int candidateId,string eventType,float eventValue,string? metadataJson=null,CancellationToken ct=default)
    {cancellation.Cancel();throw new OperationCanceledException(ct);}
}
internal sealed class FailClaimLoad:DbCommandInterceptor
{
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,CommandEventData data,InterceptionResult<DbDataReader> result,CancellationToken ct=default)
    {if(command.CommandText.Contains("match_afterthoughts"))throw new InvalidOperationException("Injected claim-load failure");return base.ReaderExecutingAsync(command,data,result,ct);}
}
internal sealed class ForbidAuthentication:IAuthenticationService
{
    public Task<AuthenticateResult> AuthenticateAsync(HttpContext c,string? s)=>Task.FromResult(AuthenticateResult.NoResult());
    public Task ChallengeAsync(HttpContext c,string? s,AuthenticationProperties? p){c.Response.StatusCode=401;return Task.CompletedTask;}
    public Task ForbidAsync(HttpContext c,string? s,AuthenticationProperties? p){c.Response.StatusCode=403;return Task.CompletedTask;}
    public Task SignInAsync(HttpContext c,string? s,ClaimsPrincipal u,AuthenticationProperties? p)=>throw new NotSupportedException();
    public Task SignOutAsync(HttpContext c,string? s,AuthenticationProperties? p)=>throw new NotSupportedException();
}
