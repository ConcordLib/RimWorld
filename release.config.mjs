import { readFileSync } from 'node:fs';

import { declaredVersions, releaseConfig } from '@rimworks/mod-ci';

const WORKSHOP_ID = process.env.WORKSHOP_ID || '3758333473';

/** @type {import('semantic-release').GlobalConfig} */
export default releaseConfig({
  solution: 'Concord.RimWorld.slnx',
  versions: declaredVersions(readFileSync('loadFolders.xml', 'utf8')),
  mods: [{
    name: 'Concord',
    title: 'Concord: runtime patching library for RimWorld mods',
    workshopId: WORKSHOP_ID,
  }],
  assets: [{ path: 'dist/Concord-*.zip', label: 'Concord mod' }],
});
