import assert from 'node:assert/strict';
import { test } from 'node:test';
import { mediaKind, parseFile, withCaption } from '../src/media.ts';

test('photos and mp4 videos show inline; everything else is a document', () => {
  assert.equal(mediaKind('image/jpeg'), 'image');
  assert.equal(mediaKind('IMAGE/PNG'), 'image');
  assert.equal(mediaKind('video/mp4'), 'video');
  assert.equal(mediaKind('image/gif'), 'document');
  assert.equal(mediaKind('application/pdf'), 'document');
  assert.equal(mediaKind('application/vnd.ms-excel'), 'document');
});

test('withCaption keeps the same upload and only changes the caption', () => {
  const prepared = { imageMessage: { url: 'https://mmg/x', mediaKey: new Uint8Array([1, 2]), caption: 'old' } };
  const a = withCaption(prepared, 'Hi Asha');
  const b = withCaption(prepared, 'Hi Bina');
  assert.equal(a.imageMessage?.caption, 'Hi Asha');
  assert.equal(b.imageMessage?.caption, 'Hi Bina');
  assert.equal(a.imageMessage?.url, 'https://mmg/x');
  assert.equal(a.imageMessage?.mediaKey, prepared.imageMessage.mediaKey);
  assert.equal(prepared.imageMessage.caption, 'old'); // the shared upload is not changed
});

test('withCaption works for documents and videos', () => {
  assert.equal(withCaption({ documentMessage: { fileName: 'a.pdf' } }, 'x').documentMessage?.caption, 'x');
  assert.equal(withCaption({ videoMessage: {} }, 'y').videoMessage?.caption, 'y');
  assert.throws(() => withCaption({}, 'z'));
});

test('parseFile accepts a valid reference and rejects bad ones', () => {
  const id = '3f2504e0-4f89-11d3-9a0c-0305e82c3301';
  assert.deepEqual(parseFile({ id, name: 'a.pdf', mimeType: 'application/pdf' }), { id, name: 'a.pdf', mimeType: 'application/pdf' });
  assert.equal(parseFile(undefined), null);
  assert.equal(parseFile(null), null);
  assert.throws(() => parseFile({ id: 'nope', name: 'a', mimeType: 'a/b' }));
  assert.throws(() => parseFile({ id, name: '', mimeType: 'a/b' }));
  assert.throws(() => parseFile({ id, name: 'a', mimeType: 'pdf' }));
  assert.throws(() => parseFile('x'));
});
