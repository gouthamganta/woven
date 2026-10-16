using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.Core.Pipeline;
using Azure.Storage.Blobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Pgvector;
using WovenBackend.Data;
using WovenBackend.Data.Entities;
using WovenBackend.Services;
using WovenBackend.Services.Embeddings;
using WovenBackend.Services.Moderation;
using WovenBackend.Services.Trust;

// All HTTP uses fake handlers. No provider, blob account, real photo or API key.
var outcomes=new List<object>();
await Run("async_embedding_prediction_is_polled_and_persisted",async()=>
{
    await using var db=Db();var handler=new ProviderFixture("async_embedding");
    var id=await Service(db,handler).EmbedPhotoAsync(1,"https://fixture.invalid/photo");
    Check(id.HasValue&&await db.PhotoEmbeddings.CountAsync()==1,"Default async prediction was dropped instead of polled; id="+id+", pollGets="+handler.PollGets);
});
await Run("immediate_attribute_predictions_are_parsed",async()=>
{
    await using var db=Db();var handler=new ProviderFixture("immediate_attributes");
    var id=await Service(db,handler).EmbedPhotoAsync(1,"https://fixture.invalid/photo");Check(id.HasValue,"Fixture embedding not saved");
    var row=await db.PhotoEmbeddings.SingleAsync();using var attributes=JsonDocument.Parse(row.AttributesJson!);
    Check(attributes.RootElement.EnumerateObject().Count()==3,"Succeeded prediction with output but no urls.get lost attributes: "+row.AttributesJson);
});
await Run("async_attribute_predictions_three_categories_persist",async()=>
{
    await using var db=Db();var handler=new ProviderFixture("async_attributes");
    var id=await Service(db,handler).EmbedPhotoAsync(1,"https://fixture.invalid/photo");Check(id.HasValue,"Fixture embedding not saved");
    var row=await db.PhotoEmbeddings.SingleAsync();using var attributes=JsonDocument.Parse(row.AttributesJson!);using var scores=JsonDocument.Parse(row.ConfidenceScoresJson!);
    Check(attributes.RootElement.EnumerateObject().Count()==3&&scores.RootElement.EnumerateObject().Count()==3,"Incomplete async attributes");
});
await Run("classification_wrong_length_is_rejected",()=>
{
    var method=typeof(PhotoEmbeddingService).GetMethod("ParseZeroShotResult",BindingFlags.Static|BindingFlags.NonPublic)!;
    var result=((string?,float))method.Invoke(null,new object[]{"{\"output\":[0.1,0.9,0.4,0.2]}",new[]{"black hair","brown hair"}})!;
    Check(result.Item1==null,"Probability count does not match labels but classifier accepted "+result.Item1);return Task.CompletedTask;
});
await Run("jpeg_scan_payload_survives_exif_strip",()=>
{
    // Synthetic JPEG marker stream: APP1, SOS header, entropy bytes, EOI.
    byte[] input={255,216,255,225,0,4,170,187,255,218,0,4,0,0,17,34,51,255,217};
    byte[] expected={255,216,255,218,0,4,0,0,17,34,51,255,217};
    var strip=typeof(PhotoEmbeddingService).GetMethod("StripExif",BindingFlags.Static|BindingFlags.NonPublic)!;
    var actual=(byte[])strip.Invoke(null,new object[]{input})!;
    Check(actual.SequenceEqual(expected),"JPEG scan payload truncated; expected bytes="+expected.Length+", actual="+actual.Length);return Task.CompletedTask;
});
foreach(var container in Enum.GetValues<MediaContainerType>())
foreach(var moderation in new[]{ModerationImageResult.APPROVED,ModerationImageResult.AUTO_REJECTED})
    await Run("upload_"+container+"_"+moderation+"_queue_gating",async()=>
    {
        var queue=new QueueFixture();var mod=DispatchProxy.Create<IModerationService,ModerationFixture>();((ModerationFixture)(object)mod).Result=moderation;
        var blob=new BlobServiceClient(new Uri("https://blob.fixture.invalid"),new AzureSasCredential("fixture"),new BlobClientOptions{Transport=new HttpClientTransport(new HttpClient(new BlobFixture()))});
        var media=new MediaService(blob,mod,DispatchProxy.Create<ITrustService,Unexpected>(),queue,NullLogger<MediaService>.Instance);
        var result=await media.ConfirmUploadAsync(1,"synthetic.jpg",container);
        var rejected=container==MediaContainerType.ProfilePhoto&&moderation==ModerationImageResult.AUTO_REJECTED;
        Check(result.Success!=rejected,"Unexpected upload result: "+result.Error);
        Check(queue.Calls==(container==MediaContainerType.ProfilePhoto&&!rejected?1:0),"Unexpected provider queue routing");
    });
await Run("worker_survives_one_failure_and_processes_next",async()=>
{
    var fake=new WorkerFixture();var services=new ServiceCollection();services.AddSingleton<IPhotoEmbeddingService>(fake);using var provider=services.BuildServiceProvider();
    using var worker=new PhotoEmbeddingWorker(provider.GetRequiredService<IServiceScopeFactory>(),NullLogger<PhotoEmbeddingWorker>.Instance);
    await worker.StartAsync(CancellationToken.None);await worker.QueuePhotoAsync(1,"one");await worker.QueuePhotoAsync(2,"two");
    await fake.Second.Task.WaitAsync(TimeSpan.FromSeconds(5));await worker.StopAsync(CancellationToken.None);Check(fake.Calls==2,"Worker stopped after failure");
});
await Run("missing_key_does_not_send_provider_prediction",async()=>
{
    await using var db=Db();var handler=new ProviderFixture("immediate_attributes");
    var service=Service(db,handler,false);var result=await service.EmbedPhotoAsync(1,"https://fixture.invalid/photo");
    Check(result==null&&handler.Posts==0,"Missing key submitted provider prediction");
});
var report=new{application="d10467e0e5d69157e22dec4c03be642fefb42f9e",transport="HTTP fixtures only; zero external calls",outcomes};
var output=Environment.GetEnvironmentVariable("WOVEN_QA_PHOTO_REPORT");if(output!=null)await File.WriteAllTextAsync(output,JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
Environment.ExitCode=outcomes.Any(x=>JsonSerializer.Serialize(x).Contains("\"outcome\":\"failed\""))?1:0;
async Task Run(string name,Func<Task> body){try{await body();var r=new{name,outcome="passed",error=(string?)null};outcomes.Add(r);Console.WriteLine(JsonSerializer.Serialize(r));}catch(Exception e){var r=new{name,outcome="failed",error=e.GetType().Name+": "+e.Message};outcomes.Add(r);Console.WriteLine(JsonSerializer.Serialize(r));}}
static void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
static PhotoContext Db()=>new(new DbContextOptionsBuilder<WovenDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
static PhotoEmbeddingService Service(WovenDbContext db,ProviderFixture handler,bool key=true)
{
    var provider=new ServiceCollection().BuildServiceProvider();
    var config=new ConfigurationBuilder().AddInMemoryCollection(key?new Dictionary<string,string?>{{"Replicate:ApiToken","fixture-only"}}:new Dictionary<string,string?>()).Build();
    return new(db,new HttpClient(handler),config,provider.GetRequiredService<IServiceScopeFactory>(),NullLogger<PhotoEmbeddingService>.Instance);
}
sealed class PhotoContext(DbContextOptions<WovenDbContext> options):WovenDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder b)
    {
        foreach(var entity in b.Model.GetEntityTypes().ToList())if(entity.ClrType!=typeof(PhotoEmbedding)&&entity.ClrType!=typeof(User))b.Ignore(entity.ClrType);
        // Projection for HTTP/attribute behavior only. PostgreSQL vector storage
        // and its dimensional constraint are outside this fixture suite.
        b.Entity<PhotoEmbedding>().Ignore(x=>x.Embedding);
    }
}
sealed class ProviderFixture(string mode):HttpMessageHandler
{
    public int Posts;public int PollGets;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct)
    {
        if(r.RequestUri!.Host=="fixture.invalid")return new(HttpStatusCode.OK){Content=new ByteArrayContent(new byte[]{1,2,3})};
        if(r.RequestUri.Host!="api.replicate.com")throw new InvalidOperationException("Unexpected fixture host");
        if(r.Method==HttpMethod.Get){PollGets++;var length=r.RequestUri.Query.StartsWith("?count=")?int.Parse(r.RequestUri.Query[7..]):512;return Json(new{status="succeeded",output=Enumerable.Repeat(.2f,length).ToArray()});}
        Posts++;using var request=JsonDocument.Parse(await r.Content!.ReadAsStringAsync(ct));var input=request.RootElement.GetProperty("input");
        if(!input.TryGetProperty("text",out var text))
        {
            if(mode=="async_embedding")return Json(new{status="starting",output=(object?)null,urls=new{get="https://api.replicate.com/v1/predictions/synthetic"}});
            return Json(new{status="succeeded",output=Enumerable.Repeat(.2f,mode=="wrong_dimension"?2:512).ToArray()});
        }
        var count=text.GetString()!.Split(',').Length;var output=Enumerable.Repeat(1f/count,count).ToArray();
        if(mode=="async_attributes")return Json(new{status="starting",output=(object?)null,urls=new{get="https://api.replicate.com/v1/predictions/synthetic?count="+count}});
        return Json(new{status="succeeded",output});
    }
    static HttpResponseMessage Json(object o)=>new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(o),Encoding.UTF8,"application/json")};
}
public class Unexpected:DispatchProxy{protected override object? Invoke(MethodInfo? m,object?[]? a)=>throw new NotSupportedException(m!.Name);}
public class ModerationFixture:DispatchProxy
{
    public ModerationImageResult Result;
    protected override object? Invoke(MethodInfo? m,object?[]? a)=>m!.Name==nameof(IModerationService.ModerateImageAsync)?Task.FromResult(Result):throw new NotSupportedException(m.Name);
}
sealed class QueueFixture:IPhotoEmbeddingQueue{public int Calls;public ValueTask QueuePhotoAsync(int u,string p){Calls++;return ValueTask.CompletedTask;}}
sealed class BlobFixture:HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct)
    {var response=new HttpResponseMessage(r.Method==HttpMethod.Delete?HttpStatusCode.Accepted:HttpStatusCode.OK){Content=new ByteArrayContent(Array.Empty<byte>())};response.Headers.Add("x-ms-blob-type","BlockBlob");return Task.FromResult(response);}
}
sealed class WorkerFixture:IPhotoEmbeddingService
{
    public int Calls;public TaskCompletionSource Second=new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task<int?> EmbedPhotoAsync(int u,string p,CancellationToken ct=default){Calls++;if(Calls==1)throw new InvalidOperationException("Injected worker failure");Second.TrySetResult();return Task.FromResult<int?>(2);}
}
