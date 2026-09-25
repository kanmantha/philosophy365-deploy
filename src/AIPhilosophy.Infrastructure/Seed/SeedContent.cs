namespace AIPhilosophy.Infrastructure.Seed;

public record SeedScript(
    int Day,
    string Title,
    string Subtitle,
    string Philosopher,
    string Category,
    string Body,
    string VideoPrompt,
    string Description,
    string Hashtags);

/// <summary>
/// The first 30 (fully handcrafted) days plus expanding themes for the full 365-day year.
/// </summary>
public static class SeedContent
{
    public static IReadOnlyList<SeedScript> Build365()
    {
        var list = new List<SeedScript>();
        list.AddRange(Days1To30);
        list.AddRange(GenerateDays(31, 365));
        return list;
    }

    private static readonly string BaseTags = "#Philosophy #DailyWisdom #Stoicism #365DaysOfPhilosophy #Mindfulness #AncientWisdom #SelfImprovement";

    private static IReadOnlyList<SeedScript> Days1To30 => new List<SeedScript>
    {
        new(1, "You Are What You Repeat", "The power of daily habits",
            "Aristotle", "Ethics",
            "We are not what we think we are. We are what we repeatedly do. Excellence is not a single heroic act, it is a habit. Every morning you face a choice, and every small choice compounds into the person you become. Ask yourself today: what will you practice? Your future self is watching. Begin now, imperfectly, and let repetition carve the path.",
            "A person walking up a spiral staircase at dawn, golden sunrise light through classical temple columns, dust particles floating, cinematic, contemplative, 4k.",
            "Excellence is not an act but a habit (Aristotle). A 45-second daily philosophy video about how small repeated choices shape who you become.",
            BaseTags),

        new(2, "The Obstacle Is the Way", "Turn difficulty into advantage",
            "Marcus Aurelius", "Stoicism",
            "The obstacle in the path becomes the path. Never forget this within the reminder, remember that the impediment to action advances action. What stands in the way becomes the way. Today's difficulty is not blocking you, it is teaching you. Adjust your lens, and a wall becomes a door. Face the thing you fear, and it shrinks. Move forward through it.",
            "A lone hiker climbing a steep rocky mountain face, storm clouds parting, ray of light hitting the summit, epic wide shot, motivational, cinematic.",
            "Marcus Aurelius on turning obstacles into advantages - 'the impediment to action advances action'. A Stoic daily insight.",
            BaseTags),

        new(3, "The Unexamined Life", "Why self-inquiry matters",
            "Socrates", "Classical",
            "An unexamined life is not worth living. But what does examining your life really mean? It means asking the hard questions: Why do I believe what I believe? Why do I act the way I act? It means refusing to drift on autopilot. Today, take ten minutes. Question one belief you inherited. That pause is where wisdom begins.",
            "A candle in a dim library, old leather books, a person's silhouette reading at a desk beside a window with moonlight, chiaroscuro lighting, thoughtful atmosphere.",
            "Socrates reminds us that self-inquiry is the beginning of wisdom. Question one belief today. A daily philosophy short.",
            BaseTags),

        new(4, "Memento Mori", "Remember you will die",
            "Stoic Tradition", "Stoicism",
            "You could leave life right now. Let that determine what you do and say and think. Most of us live as if tomorrow is guaranteed. It is not. Memento Mori, remember that you must die, sounds grim, but it is the ultimate life hack. Awareness of death cuts through noise and focuses the mind on what truly matters. Let the hourglass sharpen your attention.",
            "An hourglass before a vast ocean at twilight, waves gently lapping, moody sky with stars emerging, an old Greek statue partially buried in sand, cinematic still.",
            "Memento Mori: 'You could leave life right now.' How remembering death sharpens your life. Daily Stoic reflection.",
            BaseTags),

        new(5, "The Potter's Clay", "We become what we shape",
            "Epictetus", "Stoicism",
            "You are the sculptor, the clay is your mind. No man is free who is not master of himself. The externals, wealth, praise, insults, are not yours to command, but your judgments are. Today, when something annoys you, pause. Between the event and the reaction lives your power. Choose the shape you press into the clay.",
            "Close-up of hands sculpting a clay head in a sunlit artist studio, particles in light beams, warm tones, slow motion, artistic.",
            "Epictetus: you are master of your judgments. Freedom begins inside. A practical Stoic daily thought.",
            BaseTags),

        new(6, "The Cave and the Shadows", "Dare to see what is real",
            "Plato", "Classical",
            "Plato imagined prisoners chained in a cave, watching shadows and believing them to be the whole of reality. One prisoner escapes, sees the sun, and returns to free the others. Here is the question: when did you last question a shadow you were told was real? The escape begins the moment you doubt what everyone assumes. Look past the wall today.",
            "A dark cave interior with silhouettes facing a glowing wall of moving shadows, one figure turning toward a blinding mouth of daylight, dramatic contrast, allegorical.",
            "Plato's allegory of the cave: dare to turn toward the light and question the shadows. A 45-second philosophical classic.",
            BaseTags),

        new(7, "Virtue Is Its Own Reward", "Why character beats outcome",
            "Seneca", "Stoicism",
            "Seneca said true happiness is to enjoy the present, without anxious dependence upon the future. We chase results, promotions, applause. But the Stoics insisted that virtue, the quality of your character and choices, is the only real good. Outcomes are weather. Character is climate. Build today a decision you would make proudly even if nobody ever saw it.",
            "A lone figure standing firm in a field during a wild storm, roots and hair whipping, but posture calm and unbent, dramatic sky, cinematic resilience.",
            "Seneca on why character, not outcome, is the only true good. A reminder to prize integrity over applause.",
            BaseTags),

        new(8, "The Ship of Theseus", "Are you still you?",
            "Ancient Greece", "Metaphysics",
            "If you replace every plank of a ship, is it still the same ship? Your body replaces its cells, your beliefs shift, your memories fade and grow. So what makes you, you today? The honest answer: a story your mind keeps retelling. You are not fixed. You are a process. And a process can be improved. What plank will you replace this year?",
            "An old wooden sailing ship being repaired plank by plank by craftsmen in a harbor morning light, symmetry, philosophical illustration style, calm sea.",
            "The Ship of Theseus and the shape-shifting nature of identity. A daily philosophy short on change and the self.",
            BaseTags),

        new(9, "Attend to Your Own Garden", "Less judgment, more action",
            "Voltaire", "Ethics",
            "At the end of Candide, after endless speculation about the best of all worlds, Voltaire's hero simply says: we must cultivate our own garden. Stop auditing the world's flaws long enough to water your own soil. Judgment is cheap; tending is work. Today, skip the complaint, do one useful deed, and let the harvest be your philosophy.",
            "Hands planting seeds in rich dark soil in soft morning garden light, dew on leaves, a shovel resting nearby, wholesome, warm, detailed macro.",
            "Voltaire's 'cultivate your own garden' - the antidote to endless complaining. A practical daily philosophy.",
            BaseTags),

        new(10, "The Happy Life Requires Little", "Desire less to need more",
            "Epicurus", "Hellenistic",
            "Epicurus taught that we chase wealth because we mistake luxury for happiness. But nothing is enough for the man to whom enough is too little. Hunger before food, thirst before water, rest before sleep: these are pleasures available to everyone. Subtract the manufactured wants and what remains is a genuinely good life. Simplify one area today.",
            "A simple rustic table with bread, olives, a jug of water and fruit, warm Mediterranean light, minimalist still life, invitation to simplicity.",
            "Epicurus on why a good life needs very little. Less desire, more delight. A daily philosophy video.",
            BaseTags),

        new(11, "The Duality of the Dancing Star", "Chaos births creation",
            "Friedrich Nietzsche", "Existentialism",
            "Nietzsche said you must have chaos in your soul to give birth to a dancing star. We fear disorder, mistakes, uncertainty, and yet every breakthrough begins in chaos. The dancer's beauty hides years of imbalance. Do not sterilize your life. Let the storm churn, then let it dance. The mess you are in may be the raw material of your finest work.",
            "A swirling nebula of stars forming the silhouette of a human figure dancing, cosmic colors purple and gold, ethereal and vast, cinematic space art.",
            "Nietzsche on how creative chaos births the dancing star within you. A bold daily existential short.",
            BaseTags),

        new(12, "The Leap of Faith", "Act before certainty",
            "Søren Kierkegaard", "Existentialism",
            "Kierkegaard said life can only be understood backwards, but it must be lived forwards. You will never have full certainty. This is what he called the leap of faith: committing fully while standing on the edge of the unknown. Decision is the door. To love, to create, to begin, you must jump before the ground proves solid. What is your leap?",
            "A person standing at the edge of a cliff at sunrise over a sea of clouds, taking a step into open air, lens flare, triumphant, cinematic.",
            "Kierkegaard's leap of faith: freedom begins when you act before certainty. A daily existential courage short.",
            BaseTags),

        new(13, "The Trolley and the Will", "No choice, then the choice is everything",
            "Simone de Beauvoir", "Existentialism",
            "We are condemned to be free, de Beauvoir warned, and that freedom is a burden. There is always a choice, even when we pretend otherwise. Not deciding is itself a decision. Every act builds the person you become, so the sum of small, honest choices is the architecture of a life. Today, own one choice you had been avoiding.",
            "An intricate branch of a vast tree splitting into two paths at a crossroads, a lone figure choosing, soft fog, morning light through leaves, symbolic.",
            "De Beauvoir: we are condemned to be free. Philosophy short on radical responsibility and choosing your life.",
            BaseTags),

        new(14, "The Library of Babel", "Limits sharpen the mind",
            "Jorge Luis Borges", "Thinking",
            "Borges imagined a library containing every possible book, infinite pages of meaning and noise. But a mind with everything finds nothing. Constraints, deadlines, boundaries, they are not prison, they are lenses. A sonnet's fourteen lines produce Shakespeare. A single day, this one day, is your fourteen lines. Write something worth keeping in it.",
            "An endless infinite library with towering shelves receding into darkness, a single warm lamplight at a reading desk, surreal, awe-inspiring, vertical.",
            "Borges and the infinite library: how limits and constraints sharpen genius. A daily philosophy short on creativity.",
            BaseTags),

        new(15, "Neither Slave Nor Master", "The middle way",
            "The Buddha", "Eastern",
            "Siddhartha taught the middle way, a path between indulgence and self-denial. Suffering often comes from craving: we grasp what fades and flee what arrives. The practice is simple and hard: see clearly, want wisely. You do not need to renounce the world, only loosen your grip on it. Today, hold one thing lightly.",
            "A golden Buddha statue in a misty forest clearing, a single lotus floating on still dark water, dawn light rays, serene and tranquil, Zen atmosphere.",
            "The Buddha's middle way between craving and austerity. Daily mindfulness short on letting go of grasping.",
            BaseTags),

        new(16, "The Axe and the Tree", "Forget the sharpening man",
            "Lao Tzu", "Eastern",
            "Lao Tzu wrote that the heavy axe falls, but the tree that bends with wind survives the storm. Rigidity breaks; flexibility endures. The wise warrior avoids the battle he can sidestep. You fought hard for today, now let Tao work. Yield where yielding costs nothing, and conserve strength for the fight that matters. Water wins by wearing stone.",
            "A massive old pine bending gracefully in a mountain storm over a river, at its base calmly flowing water, mist, Chinese ink painting style, poetic.",
            "Lao Tzu's Tao Te Ching on flexibility and yielding strength. A daily Eastern philosophy short.",
            BaseTags),

        new(17, "Cogito, Ergo Sum", "You are thinking, therefore you are",
            "René Descartes", "Modern",
            "Descartes doubted everything, the world, the body, even reason. But the one thing he could not doubt was the doubting itself. I think, therefore I am. Strip away all assumptions and you still stand: a being that questions. Your doubts are not your weakness; they are proof that you are awake. Build your castle on that ground.",
            "A lone figure standing on a floating stone platform above swirling mathematical clouds, a glowing geometric cube rotating in their hand, surreal minimalist, Cartesian.",
            "Descartes' radical doubt and the foundation 'I think, therefore I am'. Daily modern philosophy short.",
            BaseTags),

        new(18, "The Radically Free", "You are not a machine",
            "Jean-Paul Sartre", "Existentialism",
            "We exist before we define ourselves. You are not born brave, honest, or doomed. You become it. Sartre said man is condemned to be free, because no script was written for you. That can terrify, and it can liberate. If no one wrote your role, then today you get to improvise. What character do you choose to play?",
            "A stage with a single spotlight, an empty chair center stage, a rising curtain parting to reveal a vast starry audience void, theatrical and vast, striking composition.",
            "Sartre: existence precedes essence. You write your own definition. A bold daily existential short.",
            BaseTags),

        new(19, "The Bronze Rule of Betterment", "Compete with yourself",
            "Guru Nanak", "Ethics",
            "Guru Nanak taught that the highest competition is not against others but against your own imperfections. Yesterday's you is your rival, and today's you is the challenger. The goal is not to be better than everyone; it is to be better than what you were. Comparison steals joy, self-competition builds it. Close one small gap today.",
            "A runner sprinting alongside their own translucent morning shadow, sunrise track, golden glow, motion blur, motivational, empowering.",
            "Guru Nanak on self-competition: beat the person you were yesterday. A daily Sikh wisdom short.",
            BaseTags),

        new(20, "The Weight of Today Only", "One day at a time",
            "St. Augustine", "Wisdom",
            "Do not worry about tomorrow, for tomorrow will take care of itself, echoes through many traditions, and Augustine sharpened it: the whole of time is present to God, but to you, a single day is given. Yesterday is memory, tomorrow is mystery, and today is the only currency you own. Spend it, do not hoard it. Wake up present.",
            "A hand holding a single glowing ember above an open palm in a dark quiet room, embers drifting, focus on the flame, intimate, warm, meditative.",
            "Augustine: today is the only day you truly own. A daily wisdom short on presence and attention.",
            BaseTags),

        new(21, "The Owl and the Sun", "Shadow gives depth",
            "C.G. Jung", "Psychology",
            "Jung said one does not become enlightened by imagining figures of light, but by making the darkness conscious. Your anger, envy, fear, they are not stains; they are messengers. What you refuse to look at inside quietly runs your life outside. Shine a lantern into one hidden room today. The sun is incomplete without its shadow.",
            "A majestic snowy owl flying from bright day into a deep forest of ferns and shadow, one wing golden, one in violet darkness, symbol meets realism, evocative.",
            "Jung on integrating your shadow: make the darkness conscious. A daily depth-psychology philosophy short.",
            BaseTags),

        new(22, "An Ethics Built on Facts", "Scientia and progress",
            "David Hume", "Enlightenment",
            "Hume woke philosophy from its dogmatic slumber, insisting that reason serves passion, and that facts alone never tell us what to do. We feel our way to values, then reason builds the bridge. But he also championed intellectual humility: our certainty about the world is habit, not proof. Stay curious, stay humble, and let experience teach you.",
            "A grand Enlightenment-era study with globes, telescopes and open manuscripts, sunlight through tall windows casting geometric shadows, painterly, intellectual.",
            "David Hume on reason, passion and intellectual humility. Daily Enlightenment philosophy short.",
            BaseTags),

        new(23, "The Ivory Prison of the Mind", "Question inherited walls",
            "Bertrand Russell", "Analytic",
            "Russell counseled that the whole problem with the world is that fools and fanatics are always so certain of themselves, and wiser people so full of doubts. Certainty is comfortable and dangerous. The cure is not apathy but well-placed confidence: act, verify, revise. Prefer the company of people who change your mind to those who merely agree.",
            "A vast minimalist ivory maze seen from above with a small figure walking its corridors, one section of wall dissolving into open countryside, architectural metaphor.",
            "Bertrand Russell on the dangers of fanatical certainty and the value of doubt. Daily analytic philosophy short.",
            BaseTags),

        new(24, "Amor Fati", "Love your fate",
            "Friedrich Nietzsche", "Stoicism",
            "Nietzsche demanded not merely that we endure fate but that we love it, amor fati: want nothing to be different, not forward, not backward, not in all eternity. Not resignation, but embrace. The pain, the setback, the boring Tuesday, they are all raw material. Your life's shape is the sum of what you consented to. Will you say yes?",
            "A figure wading into a glowing river of stars at midnight under a colossal Milky Way, arms open, serene surrender to the cosmos, awe-inspiring astral.",
            "Amor Fati: love your fate. Nietzsche's test of affirmation. Daily existential and Stoic short.",
            BaseTags),

        new(25, "The Golden Mean", "Virtue between extremes",
            "Aristotle", "Ethics",
            "Courage sits between cowardice and recklessness. Generosity between waste and stinginess. Aristotle called this the golden mean: excellence is not a maximum but a balance. We err by extremes, in opinions, spending, effort, rest. Today, audit one area running hot or cold and restore its middle. Moderation is not mediocrity; it is mastery.",
            "A precise balance scale at golden hour on a beach with one dish containing fire and one containing water, calm point equilibrium, symbolic, beautiful.",
            "Aristotle's golden mean: virtue is the balance between extremes. Daily ethical philosophy short.",
            BaseTags),

        new(26, "The Beauty of Purpose", "Telos in everything",
            "Aristotle", "Metaphysics",
            "Everything has a purpose, a telos: an acorn's aim is the oak, a knife's aim is the cut. Aristotle asked not what you do but what you are for. When your days serve a clear why, the how becomes bearable and even joyful. Money, status, comfort, they are instruments. Find the end you serve, and the instruments arrange themselves.",
            "Time-lapse of an acorn growing into a mighty oak across seasons, sun orbiting, rain and snow passing, roots reaching deep, epic natural storytelling.",
            "Aristotle's telos: discover the purpose you are built to serve. Daily metaphysics and purpose short.",
            BaseTags),

        new(27, "Happiness Is an Activity", "Not a destination",
            "Aristotle", "Ethics",
            "We treat happiness like a prize to be won later. Aristotle treated it like an activity done now, the life of excellent action. You do not find happiness; you practice it, in small well-chosen deeds, honest work, friendships kept, problems faced well. Today, instead of hunting happiness, perform it. Do one excellent thing plainly.",
            "Joyful detailed vignette: hands kneading bread dough in warm morning kitchen light, flour dust glowing, a friend laughing over coffee at a wooden table, cozy realism.",
            "Aristotle: happiness is an activity of soul in accordance with virtue, not a destination. A practical daily short.",
            BaseTags),

        new(28, "The Art of Beginning", "The first thread",
            "Seneca", "Stoicism",
            "It is not that we have a short time to live, Seneca wrote, but that we waste much of it. Life is long enough, and a generous amount has been given to us, if time were properly invested. The great enemy is not death but deferral. Begin the thing you keep rescheduling. Fortune favors the brave, and the started. This moment counts.",
            "A weaver's hands casting the first threads onto a vast loom as morning light floods the studio, first weave taking shape, golden detail, symbolic of beginnings.",
            "Seneca on life's length and the art of beginning now. Daily Stoic short on escaping procrastination.",
            BaseTags),

        new(29, "The Community of Reason", "We rise together or fall alone",
            "Aristotle", "Politics",
            "Man is a political animal, Aristotle insisted. We are not self-made; we are co-made. Your courage draws on ancestors, your comfort on strangers, your future on friends. Stoics imagined a cosmopolis, a single city of reason where no one flourishes while others starve. Today, strengthen one bond, and watch how much of you was already made of others.",
            "Aerial golden-hour shot of an ancient agora teeming with citizens, market stalls, philosophers debating, all connected by woven threads of light, civic and warm.",
            "Aristotle on humans as political animals and our debt to the community. Daily politics/philosophy short.",
            BaseTags),

        new(30, "The Final Lesson", "Wisdom becomes giving",
            "Socrates", "Classical",
            "Socrates spent his last hours not hoarding wisdom but offering it. He taught that the beginning of wisdom is the admission: I know that I know nothing. Then he made that humility a gift, questioning youths so they might find truth themselves. Wisdom is not a museum you visit; it is a garden you tend and share. Leave something worth finding.",
            "A calm elderly mentor handing a glowing lantern to a young student in a twilight olive grove, monumental and tender, light passing from hand to hand, cinematic.",
            "Socrates' final lesson: humility and giving wisdom away. Closing the first month of 365 Days of Philosophy.",
            BaseTags)
    };

    private static IEnumerable<SeedScript> GenerateDays(int from, int to)
    {
        var pool = new[]
        {
            new ThemeEntry("The Stoic Art of Response", "Stoicism", "Marcus Aurelius"),
            new ThemeEntry("Desire and Detachment", "Eastern Thought", "The Buddha"),
            new ThemeEntry("The Power of Doubt", "Modern", "René Descartes"),
            new ThemeEntry("Freedom and Responsibility", "Existentialism", "Jean-Paul Sartre"),
            new ThemeEntry("The Ethics of Attention", "Classical", "Plutarch"),
            new ThemeEntry("The Fleeting Now", "Stoicism", "Seneca"),
            new ThemeEntry("Nature as Teacher", "Eastern Thought", "Lao Tzu"),
            new ThemeEntry("The Fragile Self", "Metaphysics", "David Hume"),
            new ThemeEntry("Courage Beyond Fear", "Ethics", "Aristotle"),
            new ThemeEntry("The Silence Within", "Mindfulness", "Thich Nhat Hanh"),
            new ThemeEntry("The Weight of Opinion", "Stoicism", "Epictetus"),
            new ThemeEntry("The Creative Spark", "Existentialism", "Friedrich Nietzsche"),
            new ThemeEntry("Small Acts, Great Lives", "Ethics", "Marcus Aurelius"),
            new ThemeEntry("The Clouds of Certainty", "Enlightenment", "Immanuel Kant"),
            new ThemeEntry("The Inner Citadel", "Stoicism", "Marcus Aurelius"),
            new ThemeEntry("Shared Reason", "Politics", "Aristotle"),
            new ThemeEntry("The River of Change", "Eastern Thought", "Heraclitus"),
            new ThemeEntry("The Art of Listening", "Classical", "Socrates"),
            new ThemeEntry("Purpose Over Pleasure", "Ethics", "Aristotle"),
            new ThemeEntry("The Mirror of Truth", "Psychology", "C.G. Jung")
        };

        var list = new List<SeedScript>();
        var rng = new Random(42);

        for (var day = from; day <= to; day++)
        {
            var theme = pool[(day - 1) % pool.Length];
            int rot = (day + rng.Next(0, 3)) % 3;
            var (variantTitle, body, prompt) = Rotate(theme.Title, rot);
            var subtitle = "A daily reflection";
            var title = $"Day {day}: {variantTitle}";

            list.Add(new SeedScript(
                day,
                title,
                subtitle,
                theme.Philosopher,
                theme.Category,
                body,
                prompt,
                $"{theme.Title} - a daily reflection inspired by {theme.Philosopher}. Day {day} of 365 Days of Philosophy.",
                BaseTags));
        }
        return list;
    }

    private static (string Title, string Body, string Prompt) Rotate(string themeTitle, int variant)
    {
        return variant switch
        {
            0 => (
                $"{themeTitle}, Day by Day",
                $"Every tradition reminds us that wisdom is not a destination but a practice. The mind learns not in one grand lesson but in the patient repetition of small ones. Today, choose one principle and hold it in your attention from waking to rest. Let it color how you speak, how you listen, how you pause before you react. Philosophy becomes real only when it becomes ordinary.",
                "Abstract flowing light forming an open book over a calm landscape, gold particles scattered across deep blue, meditative, elegant, 4k."),
            1 => (
                $"{themeTitle}, Revisited",
                $"You have met this idea before, maybe in another year, another mood. Great truths are like rivers you can never step into twice, for the river changes and so do you. Revisit one belief you set down long ago and see what age has added to it. The wise do not collect conclusions; they maintain a lively conversation with the ones they hold. Resume that conversation today.",
                "A winding river through mountains at first light, mist rising, a single boat navigating, vast and serene, cinematic."),
            _ => (
                $"{themeTitle}, in Practice",
                $"Ideas are tested not in the study but in the street. What would this principle look like in a difficult phone call, a crowded commute, a moment of honest feedback? Wisdom is portable armor worn inward. When feel the old reflex rising, you have found your training ground. One civilized choice today is enough; compound the small victories. Tomorrow, a fraction more.",
                "A pair of worn boots stepping onto a fresh path through autumn forest, low golden sun, mist, hopeful mood, cinematic close-up.")
        };
    }

    private readonly record struct ThemeEntry(string Title, string Category, string Philosopher);
}