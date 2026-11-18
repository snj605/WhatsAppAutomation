import { defaultWish } from '../modules/composer-preset';
import { statusLook } from '../modules/time-text';
import { fitsPerDay, windowHours } from './sending.page';

describe('sending hours', () => {
  it('counts the hours between two times, past midnight too', () => {
    expect(windowHours('09:00', '21:00')).toBe(12);
    expect(windowHours('21:00', '06:00')).toBe(9);
    expect(windowHours('00:00', '00:00')).toBe(24);
  });
});

describe('fits per day', () => {
  it('matches the server: whole chunks with rest, then the time left, never above the limit', () => {
    // 12 hours, 50 at a time with 60 s average and 15 min rest: 11 whole chunks (550) and more, capped at 300.
    expect(fitsPerDay(300, 50, 15, 30, 90, 12)).toBe(300);
    // Same as the server test: 500 a day, 100 in one go, 5 min rest, 10-20 s, 07:00-22:00.
    expect(fitsPerDay(500, 100, 5, 10, 20, 15)).toBe(500);
    // 1 hour, 50 at a time, 60 s average, 15 min rest: one chunk (3900 s > 3600) is not whole, so 3600/60 = 60, capped at 50.
    expect(fitsPerDay(500, 50, 15, 30, 90, 1)).toBe(50);
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
