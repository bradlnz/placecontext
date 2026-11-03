import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mentionParts } from '../../src/PlaceContext.Host/wwwroot/mcp-composer.js';

test('selected mentions display without syntax quotes and retain the exact routing token', () => {
  const parts = mentionParts('Ask @"Commerce MCP" and @Warehouse for orders');
  assert.equal(parts.map(part => part.text).join(''), 'Ask @Commerce MCP and @Warehouse for orders');
  assert.equal(parts.map(part => part.raw || part.text).join(''), 'Ask @"Commerce MCP" and @Warehouse for orders');
  assert.deepEqual(parts.filter(part => part.raw).map(part => part.text), ['@Commerce MCP', '@Warehouse']);
});

test('quoted names round-trip punctuation, quotes, unicode and markup as plain text', () => {
  for (const name of ['API.v2', 'MCP "quoted"', '倉庫', '<img src=x onerror=alert(1)>']) {
    const raw = '@' + JSON.stringify(name);
    const token = mentionParts(raw).find(part => part.raw);
    assert.equal(token.text, '@' + name);
    assert.equal(token.raw, raw);
  }
});

test('email addresses and incomplete quotes remain ordinary text', () => {
  for (const text of ['Email user@example.test', 'Ask @"Commerce', 'Ask @"bad\\x"']) {
    assert.equal(mentionParts(text).map(part => part.text).join(''), text);
    assert.equal(mentionParts(text).some(part => part.raw), false);
  }
});
