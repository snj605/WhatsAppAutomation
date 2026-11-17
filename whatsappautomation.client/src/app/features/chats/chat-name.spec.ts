import { chatName } from './new-chat.page';

describe('chatName', () => {
  it('uses the first words without placeholders', () => {
    expect(chatName('{Hi|Hello} {{name}}, happy Diwali 🪔')).toBe('happy Diwali 🪔');
  });

  it('shortens long text', () => {
    expect(chatName('a'.repeat(60)).length).toBe(41);
  });
});
