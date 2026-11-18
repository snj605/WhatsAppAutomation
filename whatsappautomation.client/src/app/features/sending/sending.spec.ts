import { defaultWish } from '../modules/composer-preset';
import { statusLook } from '../modules/time-text';
import { windowHours } from './sending.page';

describe('sending hours', () => {
  it('counts the hours between two times, past midnight too', () => {
    expect(windowHours('09:00', '21:00')).toBe(12);
    expect(windowHours('21:00', '06:00')).toBe(9);
    expect(windowHours('00:00', '00:00')).toBe(24);
  });
});

describe('status look', () => {
  it('names every message state the same way', () => {
    expect(statusLook('read')).toEqual({ text: 'Read', tone: 'good' });
    expect(statusLook('failed').tone).toBe('bad');
    expect(statusLook(null).text).toBe('Not sent');
  });
});

describe('default wish', () => {
  it('uses the second name when the occasion has one', () => {
    expect(defaultWish({ kind: 'yearly', name: 'Asthi', forNameLabel: 'In memory of' })).toContain('{{for_name}}');
    expect(defaultWish({ kind: 'birthday', name: 'Birthday', forNameLabel: null })).toContain('{{name}}');
  });
});
