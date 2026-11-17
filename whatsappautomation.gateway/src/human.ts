/**
 * Helpers that make sending look less like a robot. Kept free of Baileys so they can be tested on their own.
 */

/** How long to show "typing…" before a message: about 60 ms per character, 1.5 to 8 seconds, with jitter. */
export function typingDelayMs(text: string, random: () => number = Math.random): number {
  const base = text.length * 60;
  const jitter = 0.75 + random() * 0.5; // 75 % to 125 %
  return Math.round(Math.min(8_000, Math.max(1_500, base * jitter)));
}
