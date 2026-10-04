namespace Armada.Tui.Ask
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// The dashboard's Ask Armada greeting quips (empty conversation) and rotating "thinking" phrases (while a turn has
    /// produced no text yet), with typographic punctuation folded to ASCII. Thread-safe.
    /// </summary>
    public static class AskPhrases
    {
        #region Public-Members

        /// <summary>
        /// Greetings (<c>components/askGreetings.ts</c>).
        /// </summary>
        public static IReadOnlyList<string> Greetings { get; } = new List<string>
        {
            "How can Armada help?",
            "Armada is here to help!",
            "What can I help you with?",
            "What are we building today?",
            "What can we set in motion?",
            "Where should we begin?",
            "What's on your mind?",
            "Ready when you are.",
            "How can I lend a hand?",
            "What would you like to do?",
            "Let's get started.",
            "What can I do for you?",
            "Ask me anything.",
            "How can the fleet help?",
            "What shall we tackle first?",
            "At your service.",
            "What's the mission today?",
            "Let's make something happen.",
            "What are we shipping today?",
            "How can we move things forward?",
            "Ahoy! How can Armada help?",
            "Welcome aboard!",
            "Permission to help granted.",
            "The fleet stands ready.",
            "All hands on deck for you.",
            "Ready to set sail?",
            "Where to, captain?",
            "Chart a course with me.",
            "The crew awaits your orders.",
            "Signal the fleet - what's the plan?",
            "Anchors aweigh - what's first?",
            "Let's weigh anchor.",
            "Steady as she goes - how can I help?",
            "What heading shall we take?",
            "The harbor is calm and the fleet is yours.",
            "Ready to make way?",
            "Set the sails - where to?",
            "Your armada awaits.",
            "Full speed ahead - what's the goal?",
            "Hoist the colors - what's the mission?",
            "The tide is with us. What's next?",
            "Clear skies ahead. How can I help?",
            "Ready to navigate?",
            "The wheel is yours.",
            "What voyage shall we plan?",
            "Point me toward the horizon.",
            "The deck is swabbed and ready.",
            "Let's chart something great.",
            "Fair winds - how can I help?",
            "Aye aye - what do you need?",
            "What should the captains take on?",
            "Which vessel needs attention?",
            "Ready to dispatch a mission?",
            "Let's rally the fleet.",
            "A new voyage, perhaps?",
            "Which mission comes first?",
            "The captains are standing by.",
            "Ready to launch a captain?",
            "What work shall we hand off?",
            "Let's put a captain to work.",
            "Which repo shall we visit?",
            "What's next in the queue?",
            "Ready to coordinate the fleet?",
            "A mission awaits your word.",
            "Let's get a voyage underway.",
            "Who shall we send out?",
            "What deserves a captain today?",
            "Let's plan the next mission.",
            "The armada is at your command.",
            "Time to dispatch?",
            "Good to see you.",
            "Glad you're here.",
            "Let's do great work together.",
            "One question at a time - let's go.",
            "Big or small, I'm listening.",
            "Bring me your trickiest task.",
            "No task too large, no detail too small.",
            "Let's figure it out together.",
            "Here to make your day easier.",
            "Whatever you need, let's dive in.",
            "Think of me as your first mate.",
            "Two heads are better than one.",
            "Let's untangle it together.",
            "I've got your back.",
            "Let's turn ideas into action.",
            "Ready to help you get unstuck.",
            "Let's make progress.",
            "Every voyage starts with a question.",
            "Ask away - I'm all ears.",
            "Let's find the shortest course.",
            "What's cooking, captain?",
            "Beep boop - how can I help?",
            "Your wish, my command.",
            "Let's make waves.",
            "Ready to sail into it?",
            "Loaded up and shipshape.",
            "The gears are turning - what's the plan?",
            "Batteries charged, sails trimmed.",
            "Let's get this ship moving.",
            "Point, and I'll steer.",
            "Say the word, captain.",
            "All systems go.",
            "Let's chart the unknown.",
            "Ready to raise the anchor?",
            "Smooth seas and good questions ahead.",
            "What adventure are we on today?",
            "Consider it underway.",
            "Let's catch the tide.",
            "On deck and ready.",
            "Give me a heading."
        };

        /// <summary>
        /// Thinking phrases (<c>components/askThinkingMessages.ts</c>).
        /// </summary>
        public static IReadOnlyList<string> Thinking { get; } = new List<string>
        {
            "Thinking...",
            "Working on it...",
            "Just a moment...",
            "Considering...",
            "Gathering thoughts...",
            "Putting it together...",
            "Almost there...",
            "Getting oriented...",
            "Lining things up...",
            "Making progress...",
            "Assembling a response...",
            "Mulling it over...",
            "Weighing the options...",
            "Connecting the dots...",
            "Untangling this...",
            "Following the thread...",
            "Cross-referencing...",
            "Looking into it...",
            "Organizing my thoughts...",
            "Sorting through this...",
            "Taking a closer look...",
            "Mapping this out...",
            "Finding the right words...",
            "Checking my reasoning...",
            "Building an answer...",
            "Refining the response...",
            "Gathering context...",
            "Making sense of it...",
            "Working through the details...",
            "Reviewing the request...",
            "Forming a response...",
            "Preparing an answer...",
            "Reasoning through it...",
            "Exploring the possibilities...",
            "Checking the details...",
            "Distilling the useful parts...",
            "Choosing my words carefully...",
            "Double-checking the details...",
            "Bringing it into focus...",
            "Tying up the loose ends...",
            "Running one last check...",
            "Nearly finished...",
            "Preparing the response...",
            "Here comes the answer...",
            "Consulting the records...",
            "Checking the archives...",
            "Flipping through the index...",
            "Following the references...",
            "Comparing the sources...",
            "Reviewing the latest findings...",
            "Reading the relevant passages...",
            "Verifying the details...",
            "Checking the fine print...",
            "Following a promising lead...",
            "Reviewing the manifests...",
            "Consulting the logbook...",
            "Calibrating...",
            "Consulting the schematic...",
            "Aligning the cogs...",
            "Fitting the pieces together...",
            "Checking the measurements...",
            "Adjusting the gears...",
            "Tightening the loose ends...",
            "Polishing the result...",
            "Making a few adjustments...",
            "Running a quick inspection...",
            "Fastening the last bolt...",
            "Letting it simmer...",
            "Stirring the possibilities...",
            "Checking the recipe...",
            "Adding a pinch of context...",
            "Tasting for balance...",
            "Plating the answer...",
            "Bringing it to the table...",
            "Charting a course...",
            "Consulting the compass...",
            "Scanning the horizon...",
            "Plotting the coordinates...",
            "Finding the best path...",
            "Checking the star charts...",
            "Reading the map...",
            "Following the trail markers...",
            "Rounding the next bend...",
            "Nearing the destination...",
            "Signaling the fleet...",
            "Consulting the captains...",
            "Hailing the vessels...",
            "Charting the voyage...",
            "Plotting the next mission...",
            "Reviewing the merge queue...",
            "Checking the docks...",
            "Raising the signal flags...",
            "Trimming the sails...",
            "Setting a course...",
            "Sounding the depths...",
            "Consulting the harbor master...",
            "Reading the ship's log...",
            "Weighing anchor...",
            "Mustering the crew...",
            "Checking the rigging...",
            "Calling the roll...",
            "Studying the charts...",
            "Coordinating the armada...",
            "Dispatching a scout...",
            "Gathering the fleet reports...",
            "Consulting the admiral's notes...",
            "Aligning the voyages...",
            "Tallying the missions...",
            "Inspecting the worktrees...",
            "Checking the tide tables...",
            "Steering toward the answer...",
            "Marking the sea charts...",
            "Counting the vessels...",
            "Preparing the orders...",
            "Relaying the signal...",
            "Surveying the fleet...",
            "Making way...",
            "Coming about...",
            "Holding steady...",
            "Guiding it into harbor...",
            "Reviewing the captain's log...",
            "Checking on the crew...",
            "Consulting the fleet roster...",
            "Reading the wind...",
            "Setting the sails...",
            "Following the current...",
            "Keeping an even keel...",
            "Bringing the thought into port...",
            "Almost ready...",
            "Just about there...",
            "One final adjustment...",
            "Ready in a moment...",
            "The pieces are in place..."
        };

        #endregion

        #region Private-Members

        private static readonly object _Lock = new object();
        private static readonly Random _Random = new Random();

        #endregion

        #region Public-Methods

        /// <summary>
        /// A random greeting.
        /// </summary>
        /// <returns>Greeting.</returns>
        public static string RandomGreeting()
        {
            return Pick(Greetings, null);
        }

        /// <summary>
        /// A random thinking phrase, avoiding an immediate repeat of <paramref name="previous"/>.
        /// </summary>
        /// <param name="previous">Previous phrase, or null.</param>
        /// <returns>Phrase.</returns>
        public static string RandomThinking(string? previous)
        {
            return Pick(Thinking, previous);
        }

        #endregion

        #region Private-Methods

        private static string Pick(IReadOnlyList<string> list, string? previous)
        {
            if (list.Count == 0) return "Thinking...";
            lock (_Lock)
            {
                string pick = list[_Random.Next(list.Count)];
                int guard = 0;
                while (pick == previous && guard < 8 && list.Count > 1)
                {
                    pick = list[_Random.Next(list.Count)];
                    guard++;
                }

                return pick;
            }
        }

        #endregion
    }
}
