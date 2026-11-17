import { highlightPlaceholders } from './placeholders';

describe('highlightPlaceholders', () => {
  it('marks both placeholder styles', () => {
    expect(highlightPlaceholders('Hi {{name}} and {Name}')).toBe('Hi <span class="ph">name</span> and <span class="ph">name</span>');
    expect(highlightPlaceholders('{{ salutation }}')).toBe('<span class="ph">salutation</span>');
  });

  it('shows word choices as one chip', () => {
    expect(highlightPlaceholders('{Hi|Hello} {{name}}')).toBe('<span class="ph">Hi / Hello</span> <span class="ph">name</span>');
  });

  it('escapes HTML before marking', () => {
    expect(highlightPlaceholders('<b>{{name}}</b>')).toBe('&lt;b&gt;<span class="ph">name</span>&lt;/b&gt;');
  });

  it('leaves plain braces alone', () => {
    expect(highlightPlaceholders('{city}')).toBe('{city}');
  });
});

describe('highlightPlaceholders (language names)', () => {
  it('marks the per-language name placeholders', () => {
    expect(highlightPlaceholders('{{name_gu}} {name_hi}')).toBe('<span class="ph">name_gu</span> <span class="ph">name_hi</span>');
  });
});
