import { defaultWish } from '../modules/composer-preset';
import { statusLook } from '../modules/time-text';
import { averageSeconds, fitsPerDay, windowHours } from './sending.page';

describe('sending hours', () => {
  it('counts the hours between two times, past midnight too', () => {
    expect(windowHours('09:00', '21:00')).toBe(12);
    expect(windowHours('21:00', '06:00')).toBe(9);
    expect(windowHours('00:00', '00:00')).toBe(24);
  });
});

describe('fits per day', () => {
  it('matches the server: 85% of the hours at no more than one message per 20 s, never above the limit', () => {
    expect(fitsPerDay(500, 12)).toBe(500);
    expect(fitsPerDay(500, 1)).toBe(153); // same as HumanPaceTests
    expect(averageSeconds(500, 1)).toBe(20);
    expect(Math.round(averageSeconds(300, 12))).toBe(122);
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
