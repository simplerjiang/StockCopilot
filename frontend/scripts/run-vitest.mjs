import { spawn } from 'node:child_process'
import { realpath } from 'node:fs/promises'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const scriptDir = path.dirname(fileURLToPath(import.meta.url))
const frontendDir = await realpath(path.resolve(scriptDir, '..'))
const vitestEntry = path.join(
  frontendDir,
  'node_modules',
  'vitest',
  'vitest.mjs'
)

const child = spawn(process.execPath, [vitestEntry, 'run', ...process.argv.slice(2)], {
  cwd: frontendDir,
  stdio: 'inherit'
})

child.on('exit', code => {
  process.exit(code ?? 1)
})

child.on('error', error => {
  console.error(error)
  process.exit(1)
})