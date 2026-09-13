const assert = require('node:assert/strict');
const { test } = require('node:test');
const { resolveExecutable } = require('../out/executableResolver.js');

test('Windows resolver accepts the PowerShell installer command aliases', () => {
  const files = new Set(['C:\\tools\\ksr.cmd']);
  const result = resolveExecutable('kestrel', {
    platform: 'win32',
    pathValue: 'C:\\tools',
    fileExists: file => files.has(file),
  });

  assert.equal(result, 'C:\\tools\\ksr.cmd');
});

test('Windows resolver preserves the ksr.exe fallback', () => {
  const files = new Set(['C:\\tools\\ksr.exe']);
  const result = resolveExecutable('kestrel', {
    platform: 'win32',
    pathValue: 'C:\\tools',
    fileExists: file => files.has(file),
  });

  assert.equal(result, 'C:\\tools\\ksr.exe');
});
