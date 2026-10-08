import { readFileSync, writeFileSync, mkdirSync, existsSync } from 'node:fs';
import { resolve } from 'node:path';
import { root, repository, owner, api, graphql } from './github-client.mjs';

const planPath = resolve(root, 'qa/github-tasks.json');
const mapPath = resolve(root, 'qa/github-board.json');
const projectTitle = 'Woven — Product Delivery & QA';
const stages = ['Intake', 'Needs decision', 'Ready', 'In progress', 'Ready for QA', 'Changes requested', 'Blocked', 'Done'];
const roles = ['Claude', 'Codex', 'Founder'];
const mode = process.argv[2] || '--plan';
if (!['--plan', '--issues-only', '--project'].includes(mode)) throw new Error('Use --plan, --issues-only, or --project.');

function createPlan() {
  const board = readFileSync(resolve(root, 'qa/BOARD.md'), 'utf8');
  const tasks = [];
  for (const line of board.split(/\r?\n/)) {
    const m = line.match(/^\|\s*((?:QA|CL|DOC|USER)-\d+)\s*\|\s*([^|]+)\|\s*([^|]+)\|\s*(.*?)\s*\|$/);
    if (!m) continue;
    const [, id, actor, status, description] = m;
    const role = actor.includes('Codex') ? 'Codex' : actor.includes('Claude') ? 'Claude' : 'Founder';
    const done = status.trim() === 'verified' || id === 'USER-001';
    const stage = done ? 'Done' : role === 'Founder' ? 'Needs decision' : status.trim() === 'blocked' ? 'Blocked' : status.trim() === 'ready for QA' ? 'Ready for QA' : 'Ready';
    tasks.push({ id, role, stage, originalStatus: status.trim(), description, priority: ['QA-002', 'QA-004', 'CL-004', 'CL-005', 'DOC-006'].includes(id) ? 'High' : 'Normal' });
  }
  tasks.push(
    { id: 'OPS-001', role: 'Codex', stage: 'Ready', priority: 'High', description: 'Build and validate the local task dispatcher: GitHub intake → isolated Claude implementation → local checks → Codex QA → bounded repair loop. Do not enable unattended execution until account access, limits, permissions and one complete pilot are verified.' },
    { id: 'OPS-002', role: 'Founder', stage: 'Needs decision', priority: 'High', description: 'Pair phone clients with the existing local development machine. Verify Codex Remote and Claude Remote Control eligibility and a harmless read-only task. Keep the laptop awake during local sessions.' },
    { id: 'OPS-003', role: 'Codex', stage: 'Ready', priority: 'Normal', description: 'Register task sessions and progress automatically: issue number, tool, session ID/link, branch/worktree, tested commit, last activity, next action and evidence. Until implemented, agents must post explicit structured handoffs to their issue.' },
  );
  writeFileSync(planPath, `${JSON.stringify({ repository, projectTitle, stages, roles, tasks }, null, 2)}\n`);
  return { repository, projectTitle, stages, roles, tasks };
}
const plan = existsSync(planPath) ? JSON.parse(readFileSync(planPath, 'utf8')) : createPlan();
if (mode === '--plan') {
  console.log(JSON.stringify({ tasks: plan.tasks.length, repository, stages, plan: 'qa/github-tasks.json' }, null, 2));
  process.exit(0);
}

let mapping = existsSync(mapPath) ? JSON.parse(readFileSync(mapPath, 'utf8')) : { repository, project: null, issues: {} };
function save() { writeFileSync(mapPath, `${JSON.stringify(mapping, null, 2)}\n`); }
const labels = [
  ...roles.map(role => ({ name: `owner:${role.toLowerCase()}`, color: role === 'Claude' ? '7B61FF' : role === 'Codex' ? '0969DA' : 'D4A72C', description: `Responsible role: ${role}` })),
  ...stages.map(stage => ({ name: `stage:${stage.toLowerCase().replaceAll(' ', '-')}`, color: stage === 'Done' ? '2DA44E' : stage === 'Blocked' ? 'CF222E' : 'D0D7DE', description: `Delivery stage: ${stage}` })),
  { name: 'priority:high', color: 'B60205', description: 'High priority' },
  { name: 'priority:normal', color: 'BFD4F2', description: 'Normal priority' },
  { name: 'woven:delivery', color: '0E8A16', description: 'Woven shared delivery pipeline' },
];
const existingLabels = await api(`/repos/${repository}/labels?per_page=100`);
for (const label of labels) if (!existingLabels.some(l => l.name === label.name)) await api(`/repos/${repository}/labels`, 'POST', label);
const existingIssues = [];
for (let page = 1; ; page++) {
  const batch = await api(`/repos/${repository}/issues?state=all&per_page=100&page=${page}`);
  existingIssues.push(...batch.filter(i => !i.pull_request));
  if (batch.length < 100) break;
}
for (const task of plan.tasks) {
  const marker = `<!-- woven-task:${task.id} -->`;
  let issue = existingIssues.find(i => i.body?.includes(marker));
  if (!issue) {
    const body = `${marker}\n\n## Task\n${task.description}\n\n## Ownership and imported state\n- Role: **${task.role}** (role label, not a separate GitHub user).\n- Stage: **${task.stage}**.\n- Imported from qa/BOARD.md; prior status: ${task.originalStatus || 'new task'}.\n- Migration does not mean an agent session is running or imply independent verification of historical completion claims.\n\n## Acceptance and evidence\nUse the task above as scope. Before working, record concrete acceptance criteria and dependencies. Before finishing, attach change references, the tested commit, commands, actual outcomes and unresolved limits.\n\n## Session handoff\nPost agent/tool, session ID or link if available, branch/worktree, commit, result, next owner and next action. No secrets or real-user data.\n\n## Delivery rules\nClaude implements; Codex validates. Routine defects return to Claude. Meaningful product changes require founder decision. Tests and services run locally; no paid API fallback or deployment is authorized.\n\nProject stage labels are the portable task status until project field synchronization is implemented. The dispatcher is not yet active.\n`;
    issue = await api(`/repos/${repository}/issues`, 'POST', {
      title: `[${task.id}] ${task.description.replace(/\*|✅|🎉/g, '').split(/\.\s/)[0].slice(0, 125)}`,
      body, labels: ['woven:delivery', `owner:${task.role.toLowerCase()}`, `stage:${task.stage.toLowerCase().replaceAll(' ', '-')}`, `priority:${task.priority.toLowerCase()}`],
    });
    console.log(`Created ${task.id}: ${issue.html_url}`);
    if (task.stage === 'Done') issue = await api(`/repos/${repository}/issues/${issue.number}`, 'PATCH', { state: 'closed', state_reason: 'completed' });
  }
  mapping.issues[task.id] = { number: issue.number, nodeId: issue.node_id, url: issue.html_url, role: task.role, importedStage: task.stage };
  save(); // Save after each mutation so interrupted setup can resume without duplicates.
}
if (mode === '--issues-only') {
  console.log('Issue migration complete. Project creation still requires project permission.');
  process.exit(0);
}

const result = await graphql('query($login:String!){user(login:$login){id projectsV2(first:100){nodes{id number title url}}}}', { login: owner });
let project = result.user.projectsV2.nodes.find(p => p.title === projectTitle);
if (!project) project = (await graphql('mutation($owner:ID!,$title:String!){createProjectV2(input:{ownerId:$owner,title:$title}){projectV2{id number title url}}}', { owner: result.user.id, title: projectTitle })).createProjectV2.projectV2;
mapping.project = project; save();
const repo = await api(`/repos/${repository}`);
await graphql('mutation($project:ID!,$repo:ID!){linkProjectV2ToRepository(input:{projectId:$project,repositoryId:$repo}){repository{id}}}', { project: project.id, repo: repo.node_id });
await graphql('mutation($id:ID!,$description:String!,$readme:String!){updateProjectV2(input:{projectId:$id,public:false,shortDescription:$description,readme:$readme}){projectV2{id}}}', {
  id: project.id, description: 'Founder intake → Claude implementation → local checks → Codex QA → evidence → acceptance.',
  readme: '# Woven delivery\n\nSubmit work through repository Issues. Every task has one owner role and one stage. Claude implements; Codex validates; founder decides product changes. No paid provider fallback or cloud deployment.\n\nThe board tracks task state, not live sessions. Local automatic dispatch/session tracking is pending OPS-001/OPS-003.\n\nMobile: use this Project in the browser, with native Codex Remote/Claude Remote Control for local sessions. Keep the local host online.\n\nIssue labels are the portable status; update matching project fields when changing stages. Never mark Done without linked evidence. Imported closed items retain their original evidence limits.\n\nRepository issues are public; do not post tokens, private user data or sensitive unpatched exploit details. This Project is private.\n',
});
let fields = (await graphql('query($id:ID!){node(id:$id){... on ProjectV2{fields(first:50){nodes{... on ProjectV2Field{id name} ... on ProjectV2SingleSelectField{id name options{id name}}}}}}}', { id: project.id })).node.fields.nodes;
async function field(name, options) {
  let found = fields.find(f => f.name === name);
  if (!found) {
    const created = await graphql('mutation($id:ID!,$name:String!,$options:[ProjectV2SingleSelectFieldOptionInput!]){createProjectV2Field(input:{projectId:$id,name:$name,dataType:SINGLE_SELECT,singleSelectOptions:$options}){projectV2Field{... on ProjectV2SingleSelectField{id name options{id name}}}}}', { id: project.id, name, options: options.map(n => ({ name: n, color: n === 'Done' ? 'GREEN' : n === 'Blocked' ? 'RED' : 'GRAY', description: n })) });
    found = created.createProjectV2Field.projectV2Field; fields.push(found);
  }
  return found;
}
const stageField = await field('Stage', stages);
const roleField = await field('Owner role', roles);
const priorityField = await field('Priority', ['High', 'Normal']);
const statusField = fields.find(f => f.name === 'Status');
for (const task of plan.tasks) {
  const issue = mapping.issues[task.id];
  const item = (await graphql('mutation($project:ID!,$content:ID!){addProjectV2ItemById(input:{projectId:$project,contentId:$content}){item{id}}}', { project: project.id, content: issue.nodeId })).addProjectV2ItemById.item;
  async function set(f, option) {
    const opt = f?.options?.find(o => o.name === option);
    if (!opt) return;
    await graphql('mutation($project:ID!,$item:ID!,$field:ID!,$option:String!){updateProjectV2ItemFieldValue(input:{projectId:$project,itemId:$item,fieldId:$field,value:{singleSelectOptionId:$option}}){projectV2Item{id}}}', { project: project.id, item: item.id, field: f.id, option: opt.id });
  }
  // Imported values initialize new project items only; reruns preserve later progress.
  if (!issue.projectItemId) {
    await set(stageField, task.stage); await set(roleField, task.role); await set(priorityField, task.priority);
    await set(statusField, task.stage === 'Done' ? 'Done' : 'Todo');
  }
  issue.projectItemId = item.id; save();
}
mapping.fields = { stage: stageField, role: roleField, priority: priorityField, status: statusField };
save();
const viewQuery = await graphql('query($id:ID!){node(id:$id){... on ProjectV2{views(first:30){nodes{id name number layout filter}}}}}', { id: project.id });
const existingViews = viewQuery.node.views.nodes;
const viewPlans = [
  { name: 'Delivery board', layout: 'BOARD_LAYOUT', filter: '' },
  { name: 'Founder decisions', layout: 'TABLE_LAYOUT', filter: 'is:open label:owner:founder' },
  { name: 'Claude queue', layout: 'TABLE_LAYOUT', filter: 'is:open label:owner:claude' },
  { name: 'Codex QA', layout: 'TABLE_LAYOUT', filter: 'is:open label:owner:codex' },
  { name: 'Blocked work', layout: 'TABLE_LAYOUT', filter: 'is:open label:stage:blocked' },
  { name: 'Completed evidence', layout: 'TABLE_LAYOUT', filter: 'is:closed' },
];
mapping.views = [];
for (const viewPlan of viewPlans) {
  let view = existingViews.find(v => v.name === viewPlan.name);
  if (!view) view = (await graphql('mutation($project:ID!,$name:String!,$layout:ProjectV2ViewLayout!){createProjectV2View(input:{projectId:$project,name:$name,layout:$layout}){projectV2View{id name number layout filter}}}', { project: project.id, name: viewPlan.name, layout: viewPlan.layout })).createProjectV2View.projectV2View;
  view = (await graphql('mutation($view:ID!,$filter:String!,$fields:[ID!]){updateProjectV2View(input:{viewId:$view,filter:$filter,configuration:{visibleFieldIds:$fields}}){projectV2View{id name number layout filter}}}', { view: view.id, filter: viewPlan.filter, fields: fields.filter(f => ['Title', 'Status', 'Stage', 'Owner role', 'Priority', 'Labels'].includes(f.name)).map(f => f.id) })).updateProjectV2View.projectV2View;
  mapping.views.push({ ...view, url: `${project.url}/views/${view.number}` }); save();
}
console.log(`Project ready: ${project.url}`);
