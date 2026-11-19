import { describe, expect, it } from 'vitest';
import { sample } from './wa-person-preview';
import { docColor, docType, waClock, waFormat, waKind } from './wa-format';

describe('waFormat', () => {
  it('formats bold, italic, strike and monospace like WhatsApp', () => {
    expect(waFormat('Hi *Ramesh*, _happy_ ~old~ `x`')).toBe('Hi <strong>Ramesh</strong>, <em>happy</em> <s>old</s> <code>x</code>');
  });
  it('escapes HTML before formatting', () => {
    expect(waFormat('<b>x</b> & "y"')).toBe('&lt;b&gt;x&lt;/b&gt; &amp; &quot;y&quot;');
  });
  it('leaves stars inside words alone', () => {
    expect(waFormat('2*3*4')).toBe('2*3*4');
  });
  it('turns links into links', () => {
    expect(waFormat('See https://example.com/a.')).toContain('<a href="https://example.com/a" target="_blank"');
  });
});

describe('waKind and documents', () => {
  it('sends a big video as a document', () => {
    expect(waKind('video/mp4', false)).toBe('video');
    expect(waKind('video/mp4', true)).toBe('document');
    expect(waKind('image/jpeg', false)).toBe('image');
    expect(waKind('image/svg+xml', false)).toBe('document');
    expect(waKind('application/pdf', false)).toBe('document');
  });
  it('names and colours document types', () => {
    expect(docType('Invite.pdf')).toBe('PDF');
    expect(docColor('Invite.pdf')).toBe('#e5252a');
    expect(docType('noext')).toBe('FILE');
  });
});

describe('sample', () => {
  it('fills names, second name and custom fields with examples', () => {
    expect(sample('{Hi|Hello} {{name}} - {{for_name}} - {{city}}')).toBe('Hi Ramesh Patel - Late Shri Mohanbhai - [city]');
    expect(sample('{{salutation}} {{name_gu}}')).toBe('Ramesh Patel');
  });
});

describe('waClock', () => {
  it('writes the time like WhatsApp on a phone', () => {
    expect(waClock(new Date(2026, 10, 20, 20, 15))).toBe('8:15 pm');
    expect(waClock(new Date(2026, 10, 20, 0, 5))).toBe('12:05 am');
    expect(waClock(new Date(2026, 10, 20, 12, 0))).toBe('12:00 pm');
  });
});
