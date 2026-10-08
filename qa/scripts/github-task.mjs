import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { root, repository, api } from './github-client.mjs';

const [command, id, stage, role, noteFile] = process.argv.slice(2);
if (command !== 'handoff' || !id || !stage || !role || !noteFile) {
  console.error('Usage: node qa/scripts/github-task.mjs handoff TASK-ID "Ready for QA" Codex path/to/handoff.md');
  process.exit(2);
}
const mapping = JSON.parse(readFileSync(resolve(root, 'qa/github-board.json'), 'utf8'));
const number = mapping.issues[id]?.number || (/^\d+$/.test(id) ? Number(id) : null);
if (!number) throw new Error('Unknown task ID.');
const stages = ['Intake', 'Needs decision', 'Ready', 'In progress', 'Ready for QA', 'Changes requested', 'Blocked', 'Done'];
if (!stages.includes(stage) || !['Claude', 'Codex', 'Founder'].includes(role)) throw new Error('Invalid stage or owner role.');
const note = readFileSync(resolve(root, noteFile), 'utf8');
if (!note.trim()) throw new Error('A substantive handoff note is required.');
if (stage === 'Done' && !/evidence/i.test(note)) throw new Error('Done requires an evidence reference in the handoff note.');
const issue = await api(`/repos/${repository}/issues/${number}`);
const labels = issue.labels.map(l => l.name).filter(l => !l.startsWith('stage:') && !l.startsWith('owner:'));
labels.push('woven:delivery', `stage:${stage.toLowerCase().replaceAll(' ', '-')}`, `owner:${role.toLowerCase()}`);
await api(`/repos/${repository}/issues/${number}/comments`, 'POST', { body: `## Session handoff\n\n${note}\n\nNext owner: ${role}\nStage: ${stage}` });
await api(`/repos/${repository}/issues/${number}`, 'PATCH', { labels: [...new Set(labels)], state: stage === 'Done' ? 'closed' : 'open', state_reason: stage === 'Done' ? 'completed' : 'reopened' });
console.log(`Updated ${issue.html_url}. Run Project sync to mirror the stage/owner fields.`);
