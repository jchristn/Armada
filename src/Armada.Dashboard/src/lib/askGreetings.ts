// Large, friendly greeting quips shown on the blank Ask Armada screen, above the "Chatting with {model}"
// line. One is picked at random on page load. Keep every entry short, warm, and on-theme (nautical /
// fleet flavor mixed with plain friendliness) -- nothing confrontational, alarming, or risky.
export const ASK_GREETINGS: string[] = [
  // --- Plain, friendly openers ---
  'How can Armada help?',
  'Armada is here to help!',
  'What can I help you with?',
  'What are we building today?',
  'What can we set in motion?',
  'Where should we begin?',
  'What’s on your mind?',
  'Ready when you are.',
  'How can I lend a hand?',
  'What would you like to do?',
  'Let’s get started.',
  'What can I do for you?',
  'Ask me anything.',
  'How can the fleet help?',
  'What shall we tackle first?',
  'At your service.',
  'What’s the mission today?',
  'Let’s make something happen.',
  'What are we shipping today?',
  'How can we move things forward?',

  // --- Nautical greetings ---
  'Ahoy! How can Armada help?',
  'Welcome aboard!',
  'Permission to help granted.',
  'The fleet stands ready.',
  'All hands on deck for you.',
  'Ready to set sail?',
  'Where to, captain?',
  'Chart a course with me.',
  'The crew awaits your orders.',
  'Signal the fleet — what’s the plan?',
  'Anchors aweigh — what’s first?',
  'Let’s weigh anchor.',
  'Steady as she goes — how can I help?',
  'What heading shall we take?',
  'The harbor is calm and the fleet is yours.',
  'Ready to make way?',
  'Set the sails — where to?',
  'Your armada awaits.',
  'Full speed ahead — what’s the goal?',
  'Hoist the colors — what’s the mission?',
  'The tide is with us. What’s next?',
  'Clear skies ahead. How can I help?',
  'Ready to navigate?',
  'The wheel is yours.',
  'What voyage shall we plan?',
  'Point me toward the horizon.',
  'The deck is swabbed and ready.',
  'Let’s chart something great.',
  'Fair winds — how can I help?',
  'Aye aye — what do you need?',

  // --- Captain / fleet flavor ---
  'What should the captains take on?',
  'Which vessel needs attention?',
  'Ready to dispatch a mission?',
  'Let’s rally the fleet.',
  'A new voyage, perhaps?',
  'Which mission comes first?',
  'The captains are standing by.',
  'Ready to launch a captain?',
  'What work shall we hand off?',
  'Let’s put a captain to work.',
  'Which repo shall we visit?',
  'What’s next in the queue?',
  'Ready to coordinate the fleet?',
  'A mission awaits your word.',
  'Let’s get a voyage underway.',
  'Who shall we send out?',
  'What deserves a captain today?',
  'Let’s plan the next mission.',
  'The armada is at your command.',
  'Time to dispatch?',

  // --- Warm & encouraging ---
  'Good to see you.',
  'Glad you’re here.',
  'Let’s do great work together.',
  'One question at a time — let’s go.',
  'Big or small, I’m listening.',
  'Bring me your trickiest task.',
  'No task too large, no detail too small.',
  'Let’s figure it out together.',
  'Here to make your day easier.',
  'Whatever you need, let’s dive in.',
  'Think of me as your first mate.',
  'Two heads are better than one.',
  'Let’s untangle it together.',
  'I’ve got your back.',
  'Let’s turn ideas into action.',
  'Ready to help you get unstuck.',
  'Let’s make progress.',
  'Every voyage starts with a question.',
  'Ask away — I’m all ears.',
  'Let’s find the shortest course.',

  // --- Playful ---
  'What’s cooking, captain?',
  'Beep boop — how can I help?',
  'Your wish, my command.',
  'Let’s make waves.',
  'Ready to sail into it?',
  'Loaded up and shipshape.',
  'The gears are turning — what’s the plan?',
  'Batteries charged, sails trimmed.',
  'Let’s get this ship moving.',
  'Point, and I’ll steer.',
  'Say the word, captain.',
  'All systems go.',
  'Let’s chart the unknown.',
  'Ready to raise the anchor?',
  'Smooth seas and good questions ahead.',
  'What adventure are we on today?',
  'Consider it underway.',
  'Let’s catch the tide.',
  'On deck and ready.',
  'Give me a heading.',
];

// Return a random greeting, avoiding an immediate repeat of `previous` when possible.
export function randomGreeting(previous?: string): string {
  const list = ASK_GREETINGS;
  if (list.length === 0) return 'How can Armada help?';
  if (list.length === 1) return list[0];
  let pick = list[Math.floor(Math.random() * list.length)];
  let guard = 0;
  while (pick === previous && guard < 8) {
    pick = list[Math.floor(Math.random() * list.length)];
    guard += 1;
  }
  return pick;
}
