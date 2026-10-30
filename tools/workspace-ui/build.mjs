import { execFileSync } from 'node:child_process';
import { readFileSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

const here = fileURLToPath(new URL('.', import.meta.url));
const vendor = new URL('../../src/PlaceContext.Host/wwwroot/vendor/', import.meta.url);
const bundle = fileURLToPath(new URL('workspace-daisyui.css', vendor));
execFileSync(process.execPath, [
  fileURLToPath(new URL('node_modules/@tailwindcss/cli/dist/index.mjs', import.meta.url)),
  '-i', 'components.css', '-o', bundle, '--minify',
], { cwd: here, stdio: 'inherit' });

// The portal already uses --border for a color; daisyUI uses it for a width.
// Rename that one upstream variable so both design systems can coexist.
const css = readFileSync(bundle, 'utf8').replace(/--border(?![\w-])/g, '--dui-border-width');
writeFileSync(bundle, '/*! daisyUI 5.7.50 | MIT | Local prefixed components; see workspace-ui-LICENSES.txt */\n' + css);

const notices = [
  ['daisyUI 5.7.50', 'node_modules/daisyui/LICENSE'],
  ['Tailwind CSS 4.3.3', 'node_modules/tailwindcss/LICENSE'],
].map(([name, path]) => name + '\n\n' + readFileSync(new URL(path, import.meta.url), 'utf8'));
writeFileSync(new URL('workspace-ui-LICENSES.txt', vendor), notices.join('\n\n---\n\n'));
