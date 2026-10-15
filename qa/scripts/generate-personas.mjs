import { mkdirSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

const root = fileURLToPath(new URL('../fixtures/', import.meta.url));
const locations = [
  { city: 'Indianapolis', lat: 39.7684, lng: -86.1581 },
  { city: 'Austin', lat: 30.2672, lng: -97.7431 },
  { city: 'Hyderabad', lat: 17.385, lng: 78.4867 },
  { city: 'Bengaluru', lat: 12.9716, lng: 77.5946 },
];
const genders = ['female', 'male', 'nonbinary'];
const behaviors = ['consistent', 'quiet', 'fast-responder', 'slow-responder', 'returns-after-gap'];
const states = ['complete', 'new', 'incomplete', 'inactive', 'safety-review'];
const personas = Array.from({ length: 100 }, (_, i) => {
  const gender = genders[i % 3];
  const cohort = Math.floor(i / 4);
  return {
    id: `qa-${String(i + 1).padStart(3, '0')}`,
    email: `qa${String(i + 1).padStart(3, '0')}@woven.invalid`,
    displayName: `QA Persona ${i + 1}`,
    synthetic: true,
    age: [18, 21, 25, 30, 35, 45, 60][cohort % 7],
    gender,
    interestedIn: cohort % 4 === 0 ? [...genders] : [genders[(i + 1) % 3]],
    location: locations[i % 4],
    intent: ['long_term', 'relationship', 'exploring'][cohort % 3],
    preferences: { ageMin: 18, ageMax: 65, distanceMiles: [5, 25, 100][cohort % 3] },
    behavior: behaviors[cohort % behaviors.length],
    targetState: states[cohort % states.length],
    sparkBalance: [0, 1, 5, 10][(cohort + i) % 4],
    foundationalAnswers: {
      evening: ['A quiet walk and a book.', 'Cooking with friends.', 'Exploring a new place.'][cohort % 3],
      communication: ['Direct and kind conversation.', 'Time to reflect before talking.', 'Regular check-ins.'][i % 3],
      values: ['Consistency and care.', 'Curiosity and shared learning.', 'Independence and mutual respect.'][(cohort + i) % 3],
    },
    tags: ['synthetic', 'adult', `cohort-${cohort}`],
  };
});
mkdirSync(root, { recursive: true });
writeFileSync(`${root}/personas.json`, `${JSON.stringify({ schemaVersion: 1, generatorVersion: 1, seed: 'woven-qa-v1', note: 'Fixture vocabulary requires schema mapping before import. No real people or provider calls.', personas }, null, 2)}\n`);
console.log('Generated 100 deterministic synthetic persona fixtures; database unchanged.');
