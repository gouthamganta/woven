import { readFileSync, writeFileSync, existsSync } from 'node:fs';
import { resolve } from 'node:path';
import { setTimeout as delay } from 'node:timers/promises';
import { root, repository, api, graphql } from './github-client.mjs';

const path = resolve(root, 'qa/github-board.json');
const watch = process.argv.includes('--watch');
async function sync() {
  if (!existsSync(path)) throw new Error('Run Project setup first.');
  const map = JSON.parse(readFileSync(path, 'utf8'));
  if (!map.project || !map.fields) throw new Error('Project setup has not completed.');
  const issues = [];
  for (let page = 1; ; page++) {
    const batch = await api(`/repos/${repository}/issues?state=all&labels=woven%3Adelivery&per_page=100&page=${page}`);
    issues.push(...batch.filter(i => !i.pull_request));
    if (batch.length < 100) break;
  }
  let changed = 0;
  for (const issue of issues) {
    const labels = issue.labels.map(l => l.name);
    const stageLabels = labels.filter(l => l.startsWith('stage:'));
    const roleLabels = labels.filter(l => l.startsWith('owner:'));
    if (stageLabels.length > 1 || roleLabels.length > 1) {
      console.log(`Issue #${issue.number}: ambiguous stage/owner labels; no sync performed.`); continue;
    }
    const key = issue.body?.match(/<!-- woven-task:([^ ]+) -->/)?.[1] || `ISSUE-${issue.number}`;
    const previous = map.issues[key] || {};
    const stage = issue.state === 'closed' ? 'Done' : map.fields.stage.options.find(o => `stage:${o.name.toLowerCase().replaceAll(' ', '-')}` === stageLabels[0])?.name || 'Intake';
    const role = map.fields.role.options.find(o => `owner:${o.name.toLowerCase()}` === roleLabels[0])?.name || 'Founder';
    const priority = labels.includes('priority:high') ? 'High' : 'Normal';
    const signature = `${issue.state}|${stage}|${role}|${priority}`;
    if (previous.projectItemId && previous.syncSignature === signature) continue;
    const item = (await graphql('mutation($project:ID!,$content:ID!){addProjectV2ItemById(input:{projectId:$project,contentId:$content}){item{id}}}', { project: map.project.id, content: issue.node_id })).addProjectV2ItemById.item;
    for (const [field, value] of [[map.fields.stage, stage], [map.fields.role, role], [map.fields.priority, priority], [map.fields.status, stage]]) {
      const option = field?.options.find(o => o.name.toLowerCase() === value.toLowerCase());
      if (!option) continue;
      await graphql('mutation($project:ID!,$item:ID!,$field:ID!,$option:String!){updateProjectV2ItemFieldValue(input:{projectId:$project,itemId:$item,fieldId:$field,value:{singleSelectOptionId:$option}}){projectV2Item{id}}}', { project: map.project.id, item: item.id, field: field.id, option: option.id });
    }
    map.issues[key] = { ...previous, number: issue.number, nodeId: issue.node_id, url: issue.html_url, role, projectItemId: item.id, syncSignature: signature };
    writeFileSync(path, `${JSON.stringify(map, null, 2)}\n`); changed++;
  }
  console.log(`Sync complete: ${issues.length} Issues checked; ${changed} Project items updated.`);
}
do {
  try { await sync(); }
  catch (error) { console.error(error.message); if (!watch) process.exitCode = 1; }
  if (watch) await delay(60000);
} while (watch);
