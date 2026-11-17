/** Ready messages for step 2. {{name}} is the person's name; {a|b} picks one word per person. */
export const SEND_TEMPLATES = [
  { label: 'Diwali', icon: 'pi pi-sun', text: '{Dear|Hello} {{name}}, wishing you and your family a very happy and prosperous Diwali 🪔' },
  { label: 'Birthday', icon: 'pi pi-gift', text: '{Happy birthday|Many happy returns of the day} {{name}}! 🎂 Wishing you a wonderful year ahead.' },
  { label: 'New Year', icon: 'pi pi-star', text: 'Happy New Year {{name}}! 🎉 Wishing you health, happiness and success in the year ahead.' },
  { label: 'Thank you', icon: 'pi pi-heart', text: 'Thank you {{name}} for being with us. 🙏' },
] as const;

/** Extra buttons under "More" in step 2. Kept in code: Angular would read "{{" in a template as its own syntax. */
export const MORE_INSERTS = [
  { label: 'Name in Gujarati', hint: 'નામ', value: '{{name_gu}}' },
  { label: 'Name in Hindi', hint: 'नाम', value: '{{name_hi}}' },
  { label: 'Name in English', hint: 'Name', value: '{{name_en}}' },
  { label: 'Salutation', hint: 'Pujya, Dear…', value: '{{salutation}}' },
  { label: 'Different greeting per person', hint: 'Hi / Hello / Namaste', value: '{Hi|Hello|Namaste}' },
] as const;

export const NAME_PLACEHOLDER = '{{name}}';
