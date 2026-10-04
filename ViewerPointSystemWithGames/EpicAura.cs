using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

// Epic Aura - cross-platform points system for Streamer.bot (Twitch, YouTube, Kick)
// One action holds this code. Present Viewers triggers, the promo Timer and all commands point at it.
// Data saved to data\EpicAura\epicaura.json in the Streamer.bot folder.
//
// Viewer commands:  !aura (!points) [name], !auratop, !give, !gamble, !duel, !accept, !deny,
//                   !heist, !fight, !ring, !enter, !bet, !contribute, !claim, !link, !unlink, !auraid,
//                   !giveaway, !predict, !goal (status when used without admin words), !aurahelp
// Admin commands:   !addaura, !aurareset, !drop, !aurainfo [n], !giveaway start|close|draw|reroll|cancel|end,
//                   !predict open|lock|result|cancel, !goal start|cancel|end, !ring cancel,
// Timed message:    add a Streamer.bot Timer (Settings > Timers) and a Timed Actions trigger on this action
public class CPHInline
{
    // ============================================================
    // CONFIG: CURRENCY AND EARNING
    // ============================================================

    private const string CurrencyName = "Epic Aura";

    private const double AuraPerMinute = 5.0;     // 300 per hour, similar to Twitch channel points
    private const double SubMultiplier = 1.5;     // Twitch subs, YouTube members, Kick subs
    private const double MaxGapMinutes = 15.0;    // gap longer than this = new session, no back pay

    // ============================================================
    // CONFIG: GAMES
    // ============================================================

    // Gamble (roll 1-100, GambleWinRoll or higher doubles your bet)
    private const long GambleMin = 10;
    private const long GambleMax = 10000;
    private const int GambleWinRoll = 56;         // 45% win chance
    private const double GambleCooldownSec = 30;

    // Duel (50/50, winner takes the bet)
    private const long DuelMin = 10;
    private const double DuelTimeoutSec = 60;

    // Heist (group game, each crew member rolls separately)
    private const long HeistMin = 50;
    private const double HeistJoinSec = 60;
    private const double HeistCooldownSec = 300;
    private const int HeistBaseChance = 35;       // % success for a solo heist
    private const int HeistChancePerMember = 4;   // extra % per extra crew member
    private const int HeistMaxChance = 55;
    private const double HeistPayout = 1.8;       // survivors get stake x this

    // Drop (first to !claim wins)
    private const double DropClaimSec = 60;

    // ============================================================
    // CONFIG: APEX FIGHT   !fight <legend> <1v1|1v2|1v3> <amount>
    // ============================================================

    private const long FightMin = 10;
    private const long FightMax = 5000;
    private const double FightCooldownSec = 20;

    // Win chance % and payout (stake included). All slightly house-favoured so Aura drains slowly.
    private static readonly Dictionary<string, FightOdds> FightSizes = new Dictionary<string, FightOdds>
    {
        { "1v1", new FightOdds(45, 2.0) },
        { "1v2", new FightOdds(29, 3.0) },
        { "1v3", new FightOdds(17, 5.0) }
    };

    private const int KraberChance = 5;           // % of wins that are Kraber headshots (double payout)
    private const int RezChance = 8;              // % of losses where a teammate pulls your banner (half back)
    private const int StreakShoutout = 3;         // wins in a row before chat hears about it
    private const int KillLeaderStreak = 5;       // wins in a row to become Kill Leader (announced everywhere)

    // "Display name|alias|alias". Add new legends here as they release.
    private static readonly string[] LegendList =
    {
        "Pathfinder|path|pathy", "Wraith", "Bloodhound|bh|blood", "Gibraltar|gibby|gib", "Lifeline|ll",
        "Bangalore|bang|banga", "Caustic", "Mirage", "Octane|oct|octavio", "Wattson|watt", "Crypto",
        "Revenant|rev", "Loba", "Rampart|ramp", "Horizon|hori", "Fuse", "Valkyrie|valk", "Seer", "Ash",
        "Mad Maggie|maggie", "Newcastle|newy", "Vantage", "Catalyst|cata", "Ballistic", "Conduit",
        "Alter", "Sparrow"
    };

    // Tokens: {legend} {size}
    private static readonly string[] FightWinLines =
    {
        "your {legend} won the {size}. Clean. Too clean. Are you on PC?",
        "{legend} won the {size} and walked off with a full shield. Disgusting.",
        "{size} won! The enemy squad is writing an angry tweet about {legend}.",
        "{legend} won the {size} without even popping a batt. Built different.",
        "W {legend}. That {size} never stood a chance.",
        "{legend} won the {size}. Somebody check this person's hardware."
    };

    private static readonly string[] FightLossLines =
    {
        "your {legend} lost the {size}. Skill issue, do better.",
        "{legend} got beamed in the {size}. Have you tried aiming?",
        "{size} lost. Your {legend} is a deathbox now. Very shiny though.",
        "{legend} pushed the {size} with 0 heals. Bold strategy. Didn't work.",
        "you lost the {size}. Ratio + deathbox + banner'd.",
        "{legend} got third partied mid {size}. Classic Apex.",
        "the {size} is over and so is your {legend}. Back to the lobby.",
        "{legend} lost the {size}. Even the Firing Range dummies are laughing."
    };

    private static readonly string[] SquadWipeLines =
    {
        "SQUAD WIPE! {legend} just 1v3'd and the lobby is in shambles!",
        "{legend} wiped a whole squad solo. Predator energy."
    };

    private static readonly string[] OneVOneLossLines =
    {
        "you lost a 1v1. A ONE v ONE. {legend} needs to go back to the Firing Range.",
        "lost the 1v1. Your {legend} is getting clipped and posted, sorry."
    };

    private static readonly Dictionary<string, string[]> LegendWinLines = new Dictionary<string, string[]>
    {
        { "Pathfinder", new[] { "Pathy ziplined in and won the {size}. Happy robot noises." } },
        { "Wraith", new[] { "Wraith phased in, won the {size}, phased out. Rude." } },
        { "Octane", new[] { "Octane stimmed into a {size} and somehow won. Pure chaos." } },
        { "Gibraltar", new[] { "Gibby domed up and bodied the {size}. Big man, big W." } },
        { "Bloodhound", new[] { "Bloodhound scanned, saw everything, won the {size}. The Allfather approves." } },
        { "Mirage", new[] { "Mirage decoyed the whole {size}. Who was real? Doesn't matter, W." } },
        { "Caustic", new[] { "Caustic gassed the {size}. Nobody likes Caustic, but they respect him now." } },
        { "Wattson", new[] { "Wattson fenced the door and won the {size}. Pylon diff." } },
        { "Seer", new[] { "Seer heartbeat sensed the whole {size}. Honestly unfair." } },
        { "Lifeline", new[] { "Lifeline won the {size} and still had time to res the whole squad." } }
    };

    private static readonly Dictionary<string, string[]> LegendLossLines = new Dictionary<string, string[]>
    {
        { "Pathfinder", new[] { "your Pathy lost the {size}. Biggest hitbox in the game, what did you expect?" } },
        { "Wraith", new[] { "your Wraith portalled into the {size} and straight back to the lobby." } },
        { "Octane", new[] { "Octane stimmed into the {size} on 20 HP. Classic Octane." } },
        { "Gibraltar", new[] { "Gibby's dome couldn't save that {size}. Too big to miss." } },
        { "Lifeline", new[] { "Lifeline sent the drone to heal and the {size} ended before it arrived." } },
        { "Bloodhound", new[] { "Bloodhound scanned the {size}, saw they were losing, lost anyway." } },
        { "Mirage", new[] { "they shot the real Mirage. Again. {size} lost." } },
        { "Revenant", new[] { "Revenant was hanging on a wall so long the {size} ended without him." } },
        { "Crypto", new[] { "Crypto was in the drone the whole {size}. His body got beamed meanwhile." } },
        { "Horizon", new[] { "Horizon lifted up and got beamed mid air. {size} lost." } },
        { "Valkyrie", new[] { "Valk ulted away from the {size}. Tactical retreat or cowardice?" } }
    };

    // ============================================================
    // CONFIG: RING (battle royale)
    // !ring <buy-in> [legend] starts a lobby, !ring [legend] joins at the same buy-in
    // ============================================================

    private const long RingMinBuyIn = 50;
    private const long RingMaxBuyIn = 10000;
    private const double RingJoinSec = 90;
    private const double RingRoundSec = 12;              // time between ring closes
    private const int RingMinPlayers = 3;                // fewer than this and everyone is refunded
    private const double RingEliminatePerRound = 0.35;   // share of the lobby knocked out each ring
    private const double RingCooldownSec = 300;
    private const int RingHouseCutPercent = 10;          // taken off the pot so Aura drains slowly
    private const int RingRunnerUpPercent = 20;          // runner up's share of the pot after the cut

    // Tokens: {name} {legend}
    private static readonly string[] RingElimLines =
    {
        "{name}'s {legend} got caught in the ring looting a deathbox.",
        "{name} got third partied while healing.",
        "{name}'s {legend} took a Kraber to the dome.",
        "{name} rotated straight into a full squad. Bad call.",
        "{name} died to ring damage holding 3 batts. Why?",
        "{name}'s {legend} got thirsted on a knockdown shield.",
        "{name} pinged an enemy, then ran straight at them.",
        "{name} hot dropped the busiest POI and never recovered.",
        "{name}'s {legend} went for the care package. The care package won.",
        "{name} got grenade spammed off a cliff.",
        "{name}'s {legend} was AFK in the dropship. Shame.",
        "{name} tried to 1v3 for content. It was not content."
    };

    private static readonly string[] UnknownLegendLines =
    {
        "that's not a legend. Are you thinking of Fortnite?",
        "never heard of that legend. Did you make them up in the dropship?"
    };

    // Linking
    private const double LinkRequestMinutes = 10.0;

    // ============================================================
    // CONFIG: PROMO (timed welcome message, !aurainfo to fire it, !aurahelp for viewers)
    // ============================================================

    private const double PromoLiveWindowMin = 15;   // timer only posts if viewers were seen this recently (so never offline)
    private const double HelpCooldownSec = 30;      // per platform, stops !aurahelp spam
    private const string HelpUrl = "";              // optional, e.g. your GitHub README link, added to the end of !aurahelp

    // Rotates in order. Keep each under 200 characters (YouTube limit).
    private static readonly string[] PromoLines =
    {
        "Welcome in! You earn Epic Aura just by hanging out here. Check yours with !aura and see who's flexing with !auratop.",
        "Got Epic Aura burning a hole in your pocket? Try !fight wraith 1v2 300, !gamble 200, !duel <name> 100 or start a battle royale with !ring 500.",
        "Watching on more than one platform? Link them with !link <platform> <name> and all your Epic Aura stacks in one wallet.",
        "Epic Aura gets you into giveaways (!enter), predictions (!bet) and community goals (!contribute). The longer you hang out, the more you can do.",
        "New here? Welcome to the Epicness fam! Say hi in chat, stack some Epic Aura and type !aurahelp to see every command.",
        "Subs and members earn 1.5x Epic Aura. Keep an eye out for drops too, first to !claim wins. Feeling generous? !give <name> <amount>."
    };

    // ============================================================
    // CONFIG: COMMANDS (lowercase, must match the Streamer.bot command text)
    // ============================================================

    // Core
    private const string CmdBalance = "!aura";
    private const string CmdBalanceAlt = "!points";
    private const string CmdTop = "!auratop";
    private const string CmdGive = "!give";
    private const string CmdLink = "!link";
    private const string CmdUnlink = "!unlink";
    private const string CmdId = "!auraid";
    private const string CmdHelp = "!aurahelp";
    private const string CmdInfo = "!aurainfo";

    // Games
    private const string CmdGamble = "!gamble";
    private const string CmdDuel = "!duel";
    private const string CmdAccept = "!accept";
    private const string CmdDeny = "!deny";
    private const string CmdHeist = "!heist";
    private const string CmdClaim = "!claim";
    private const string CmdFight = "!fight";
    private const string CmdRing = "!ring";

    // Events
    private const string CmdGiveaway = "!giveaway";
    private const string CmdEnter = "!enter";
    private const string CmdPredict = "!predict";
    private const string CmdBet = "!bet";
    private const string CmdGoal = "!goal";
    private const string CmdContribute = "!contribute";

    // Admin
    private const string CmdAdd = "!addaura";
    private const string CmdReset = "!aurareset";
    private const string CmdDrop = "!drop";

    // ============================================================
    // CONFIG: ADMINS, IGNORED ACCOUNTS, CHAT
    // ============================================================

    private const bool ModsAreAdmins = true;
    private static readonly List<string> AdminIds = new List<string>
    {
        // Use !auraid on each platform and paste the result here, e.g. "twitch:12345678"
    };

    private static readonly List<string> IgnoredNames = new List<string>
    {
        "streamelements", "nightbot", "moobot", "sery_bot", "streamlabs", "fossabot"
    };

    private static readonly string[] AnnouncePlatforms = { "twitch", "youtube", "kick" };
    private const int YouTubeMaxChars = 200;      // YouTube chat limit
    private const int OtherMaxChars = 480;

    // Storage (inside the Streamer.bot folder)
    private static readonly string DataDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "EpicAura");
    private static readonly string DataPath = Path.Combine(DataDir, "epicaura.json");

    // ============================================================
    // DATA (saved to disk)
    // ============================================================

    public class Wallet
    {
        public string Id;
        public string Name;
        public long Aura;
        public long Lifetime;                     // earned from watching and admin adds
        public double Carry;                      // fractional aura carried between ticks
        public DateTime LastAccrualUtc;
        public List<string> Accounts = new List<string>();   // "platform:userId"
    }

    public class GiveawayState
    {
        public string Prize;
        public long TicketCost;
        public int MaxTickets;
        public bool Open;
        public bool Drawn;
        public Dictionary<string, int> Tickets = new Dictionary<string, int>();   // walletId -> tickets
        public List<string> Winners = new List<string>();
    }

    public class PredictionBet
    {
        public int Option;                        // 1-based
        public long Amount;
    }

    public class PredictionState
    {
        public string Question;
        public List<string> Options = new List<string>();
        public bool Locked;
        public Dictionary<string, PredictionBet> Bets = new Dictionary<string, PredictionBet>();
    }

    public class GoalState
    {
        public string Reward;
        public long Target;
        public long Progress;
        public Dictionary<string, long> Contributors = new Dictionary<string, long>();
    }

    public class Store
    {
        public Dictionary<string, Wallet> Wallets = new Dictionary<string, Wallet>();
        public Dictionary<string, string> AccountToWallet = new Dictionary<string, string>();
        public Dictionary<string, string> AccountNames = new Dictionary<string, string>();
        public GiveawayState Giveaway;
        public PredictionState Prediction;
        public GoalState Goal;
    }

    // ============================================================
    // DATA (in memory, short lived)
    // ============================================================

    private class LinkRequest { public string FromAccount; public DateTime ExpiresUtc; }
    private class DuelRequest { public string ChallengerId; public long Amount; public DateTime ExpiresUtc; }
    private class HeistState { public Dictionary<string, long> Crew = new Dictionary<string, long>(); }
    private class DropState { public long Amount; public DateTime ExpiresUtc; }
    private class RingPlayer { public string WalletId; public string Name; public string Legend; }
    private class RingState
    {
        public long BuyIn;
        public bool Started;
        public int Round;
        public List<RingPlayer> All = new List<RingPlayer>();
        public List<RingPlayer> Alive = new List<RingPlayer>();
    }
    private class FightOdds { public int WinChance; public double Payout; public FightOdds(int w, double p) { WinChance = w; Payout = p; } }

    private class Ctx
    {
        public string Platform, Key, Name;
        public Wallet Me;
        public string[] Parts;
        public bool Admin;
    }

    private static readonly object Gate = new object();
    private static readonly Random Rng = new Random();
    private static Store store;

    private static readonly Dictionary<string, LinkRequest> linkRequests = new Dictionary<string, LinkRequest>(); // "platform:normalisedname"
    private static readonly Dictionary<string, DuelRequest> duels = new Dictionary<string, DuelRequest>();        // target walletId
    private static readonly Dictionary<string, DateTime> gambleCooldowns = new Dictionary<string, DateTime>();    // walletId
    private static HeistState heist;
    private static System.Threading.Timer heistTimer;
    private static DateTime heistCooldownUntil = DateTime.MinValue;
    private static DropState drop;
    private static readonly Dictionary<string, DateTime> fightCooldowns = new Dictionary<string, DateTime>();   // walletId
    private static readonly Dictionary<string, int> fightStreaks = new Dictionary<string, int>();             // walletId
    private static RingState ring;
    private static System.Threading.Timer ringTimer;
    private static DateTime ringCooldownUntil = DateTime.MinValue;
    private static Dictionary<string, string> legendLookup;
    private static DateTime lastPresentTickUtc = DateTime.MinValue;
    private static int promoIndex;
    private static readonly Dictionary<string, DateTime> helpCooldowns = new Dictionary<string, DateTime>();   // platform
    private static readonly List<string> loggedKeysFor = new List<string>();

    // ============================================================
    // ENTRY
    // ============================================================

    public bool Execute()
    {
        lock (Gate)
        {
            try
            {
                EnsureLoaded();

                if (args.ContainsKey("command"))
                    return HandleCommand();

                string evt = EventName();
                if (evt.IndexOf("Timer", StringComparison.OrdinalIgnoreCase) >= 0 || evt.IndexOf("Timed", StringComparison.OrdinalIgnoreCase) >= 0)
                    return HandlePromoTimer();

                if (evt.Contains("PresentViewers") || args.ContainsKey("users"))
                    return HandlePresentViewers(evt);

                CPH.LogWarn("[EpicAura] Unhandled trigger: " + evt);
            }
            catch (Exception ex)
            {
                CPH.LogError("[EpicAura] " + ex);
            }
        }
        return true;
    }

    // ============================================================
    // EARNING
    // ============================================================

    private bool HandlePresentViewers(string evt)
    {
        string platform = PlatformFrom(evt);
        if (platform == null) return true;

        lastPresentTickUtc = DateTime.UtcNow;   // used by the promo timer as a "we're live" check

        // Kick/YouTube pass isLive, skip if offline
        object live;
        if (args.TryGetValue("isLive", out live) && live is bool && !(bool)live) return true;

        var users = args.ContainsKey("users") ? args["users"] as List<Dictionary<string, object>> : null;
        if (users == null || users.Count == 0) return true;

        LogUserKeysOnce(platform, users[0]);

        DateTime now = DateTime.UtcNow;
        var paidThisTick = new List<string>();

        foreach (var u in users)
        {
            string id = Str(u, "id", "userId", "user_id");
            string login = Str(u, "login", "userName", "username");
            string name = FirstNonEmpty(Str(u, "display", "displayName", "user", "name"), login);
            if (string.IsNullOrEmpty(id) || IsIgnored(name, login)) continue;

            Wallet w = GetOrCreateWallet(platform, id, name);
            if (paidThisTick.Contains(w.Id)) continue;
            paidThisTick.Add(w.Id);   // linked accounts present on two platforms

            bool isSub = Bool(u, "subscribed", "isSubscribed", "isSponsor", "isMember");
            Accrue(w, now, isSub);
        }

        Save();
        return true;
    }

    private void Accrue(Wallet w, DateTime now, bool isSub)
    {
        double minutes = (now - w.LastAccrualUtc).TotalMinutes;

        // First sighting or long gap: start the clock, no back pay
        if (w.LastAccrualUtc == DateTime.MinValue || minutes > MaxGapMinutes || minutes < 0)
        {
            w.LastAccrualUtc = now;
            return;
        }

        double earned = minutes * AuraPerMinute * (isSub ? SubMultiplier : 1.0) + w.Carry;
        long whole = (long)Math.Floor(earned);

        w.Carry = earned - whole;
        w.Aura += whole;
        w.Lifetime += whole;
        w.LastAccrualUtc = now;
    }

    // ============================================================
    // COMMAND ROUTER
    // ============================================================

    private bool HandleCommand()
    {
        string cmd = Arg("command").Trim().ToLowerInvariant();
        string rawInput = Arg("rawInput");

        // All commands can live on ONE Streamer.bot command, so read the real command word from the message
        string msg = Arg("message").Trim();
        if (msg.StartsWith("!"))
        {
            int space = msg.IndexOf(' ');
            cmd = (space < 0 ? msg : msg.Substring(0, space)).ToLowerInvariant();
            rawInput = space < 0 ? "" : msg.Substring(space + 1);
        }

        string platform = PlatformFrom(Arg("userType")) ?? PlatformFrom(EventName()) ?? "twitch";
        string userId = Arg("userId");
        string userName = FirstNonEmpty(Arg("user"), Arg("userName"));
        if (string.IsNullOrEmpty(userId)) return true;

        var c = new Ctx
        {
            Platform = platform,
            Key = AccountKey(platform, userId),
            Name = userName,
            Parts = rawInput.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
        };
        c.Me = GetOrCreateWallet(platform, userId, userName);
        c.Admin = IsAdmin(c.Key);

        switch (cmd)
        {
            // Core
            case CmdBalance:
            case CmdBalanceAlt: ShowBalance(c); break;
            case CmdTop: ShowTop(c); break;
            case CmdGive: Give(c); break;
            case CmdLink: LinkAccounts(c); break;
            case CmdUnlink: UnlinkAccount(c); break;
            case CmdId: Say(c, "your ID is " + c.Key); break;
            case CmdHelp: ShowHelp(c); break;
            case CmdInfo: if (c.Admin) FirePromo(c); else ShowHelp(c); break;

            // Games
            case CmdGamble: Gamble(c); break;
            case CmdDuel: Duel(c); break;
            case CmdAccept: AcceptDuel(c); break;
            case CmdDeny: DenyDuel(c); break;
            case CmdHeist: Heist(c); break;
            case CmdClaim: Claim(c); break;
            case CmdFight: Fight(c); break;
            case CmdRing: Ring(c); break;

            // Events
            case CmdGiveaway: Giveaway(c); break;
            case CmdEnter: EnterGiveaway(c); break;
            case CmdPredict: Predict(c); break;
            case CmdBet: Bet(c); break;
            case CmdGoal: Goal(c); break;
            case CmdContribute: Contribute(c); break;


            // Admin
            case CmdAdd: if (c.Admin) AddAura(c); break;
            case CmdReset: if (c.Admin) ResetAll(c); break;
            case CmdDrop: if (c.Admin) StartDrop(c); break;
        }

        Save();
        return true;
    }

    // ============================================================
    // PROMO
    // Timer fires HandlePromoTimer, !aurainfo [n] fires it on demand (admin), !aurahelp lists commands
    // ============================================================

    private bool HandlePromoTimer()
    {
        if ((DateTime.UtcNow - lastPresentTickUtc).TotalMinutes > PromoLiveWindowMin)
        {
            CPH.LogInfo("[EpicAura] Promo skipped, not live");
            return true;
        }
        Announce(NextPromo());
        return true;
    }

    // !aurainfo      next message in the rotation
    // !aurainfo 3    a specific message
    private void FirePromo(Ctx c)
    {
        int n;
        if (c.Parts.Length > 0 && int.TryParse(c.Parts[0], out n) && n >= 1 && n <= PromoLines.Length)
            Announce(PromoLines[n - 1]);
        else
            Announce(NextPromo());
    }

    // Base lines plus live event reminders when a giveaway or goal is running
    private string NextPromo()
    {
        var pool = new List<string>(PromoLines);
        if (store.Giveaway != null && store.Giveaway.Open) pool.Add("Giveaway running! " + GiveawayStatus(store.Giveaway));
        if (store.Goal != null) pool.Add("Community goal! " + GoalStatus(store.Goal));

        string line = pool[promoIndex % pool.Count];
        promoIndex++;
        return line;
    }

    private void ShowHelp(Ctx c)
    {
        DateTime now = DateTime.UtcNow;
        DateTime until;
        if (helpCooldowns.TryGetValue(c.Platform, out until) && until > now) return;
        helpCooldowns[c.Platform] = now.AddSeconds(HelpCooldownSec);

        string help = $"{CurrencyName}: earn it by watching. Check {CmdBalance} {CmdTop} | Play {CmdFight} {CmdRing} {CmdGamble} {CmdDuel} {CmdHeist} | Events {CmdEnter} {CmdBet} {CmdContribute} | Link {CmdLink}";
        if (HelpUrl != "") help += " | Guide: " + HelpUrl;
        Reply(c.Platform, help);
    }

    // ============================================================
    // CORE
    // ============================================================

    private void ShowBalance(Ctx c)
    {
        if (c.Parts.Length > 0)
        {
            Wallet t = FindWalletByName(c.Platform, JoinParts(c.Parts, 0, c.Parts.Length));
            if (t == null) { Say(c, "couldn't find that person."); return; }
            Reply(c.Platform, $"{t.Name} has {Fmt(t.Aura)} {CurrencyName}.");
            return;
        }
        Say(c, $"you have {Fmt(c.Me.Aura)} {CurrencyName}.");
    }

    private void ShowTop(Ctx c)
    {
        var top = store.Wallets.Values.Where(w => w.Aura > 0).OrderByDescending(w => w.Aura).Take(5).ToList();
        if (top.Count == 0) { Reply(c.Platform, $"Nobody has any {CurrencyName} yet."); return; }

        var lines = top.Select((w, i) => $"{i + 1}. {w.Name} ({Fmt(w.Aura)})");
        Reply(c.Platform, $"Top {CurrencyName}: " + string.Join(" | ", lines));
    }

    // !give name amount
    private void Give(Ctx c)
    {
        if (c.Parts.Length < 2) { Say(c, $"usage: {CmdGive} <name> <amount>"); return; }

        Wallet t = FindWalletByName(c.Platform, JoinParts(c.Parts, 0, c.Parts.Length - 1));
        long amount = ParseAmount(c.Parts.Last(), c.Me.Aura);

        if (t == null) { Say(c, "couldn't find that person."); return; }
        if (t.Id == c.Me.Id) { Say(c, "you can't give to yourself."); return; }
        if (amount <= 0 || !TrySpend(c.Me, amount)) { Say(c, $"not enough, you have {Fmt(c.Me.Aura)}."); return; }

        t.Aura += amount;
        Say(c, $"gave {Fmt(amount)} {CurrencyName} to {t.Name}.");
    }

    // ============================================================
    // LINKING
    // !link youtube MyName   (on platform A)
    // !link confirm          (on platform B, as MyName)
    // ============================================================

    private void LinkAccounts(Ctx c)
    {
        PurgeExpiredLinks();

        // Step 2: confirm a request aimed at this account
        if (c.Parts.Length == 1 && c.Parts[0].ToLowerInvariant() == "confirm")
        {
            string reqKey = c.Platform + ":" + NormName(c.Name);
            LinkRequest req;
            if (!linkRequests.TryGetValue(reqKey, out req))
            {
                Say(c, $"no link request waiting. Start one on your other platform with {CmdLink} {c.Platform} {c.Name}");
                return;
            }
            linkRequests.Remove(reqKey);
            Say(c, MergeAccounts(req.FromAccount, c.Key));
            return;
        }

        // Step 1: request a link to an account on another platform
        if (c.Parts.Length >= 2)
        {
            string targetPlatform = PlatformFrom(c.Parts[0]);
            string targetName = JoinParts(c.Parts, 1, c.Parts.Length - 1);   // YouTube names can have spaces

            if (targetPlatform != null && targetPlatform != c.Platform)
            {
                string reqKey = targetPlatform + ":" + NormName(targetName);

                // Someone else already asked to link this name: cancel both so nobody can hijack a wallet
                LinkRequest other;
                if (linkRequests.TryGetValue(reqKey, out other) && other.FromAccount != c.Key)
                {
                    linkRequests.Remove(reqKey);
                    Say(c, "there was already a link request for that account, both cancelled for safety. Try again.");
                    return;
                }

                linkRequests[reqKey] = new LinkRequest
                {
                    FromAccount = c.Key,
                    ExpiresUtc = DateTime.UtcNow.AddMinutes(LinkRequestMinutes)
                };
                Say(c, $"now go to {Cap(targetPlatform)} as {CleanName(targetName)} and type {CmdLink} confirm within {LinkRequestMinutes} mins.");
                return;
            }
        }

        Say(c, $"usage: {CmdLink} <twitch|youtube|kick> <your name there>, then {CmdLink} confirm on that platform.");
    }

    private string MergeAccounts(string fromKey, string toKey)
    {
        string keepId, mergeId;
        if (!store.AccountToWallet.TryGetValue(fromKey, out keepId) || !store.AccountToWallet.TryGetValue(toKey, out mergeId))
            return "link failed, try again.";
        if (keepId == mergeId)
            return "those accounts are already linked.";

        Wallet keep = store.Wallets[keepId];
        Wallet merge = store.Wallets[mergeId];

        // One account per platform per wallet (stops alt pooling)
        var keepPlatforms = keep.Accounts.Select(PlatformOfKey).ToList();
        if (merge.Accounts.Any(a => keepPlatforms.Contains(PlatformOfKey(a))))
            return $"that wallet already has an account on one of those platforms. Use {CmdUnlink} first.";

        // Don't merge mid-game, stakes are tracked per wallet
        if (HasActiveStake(merge.Id))
            return "finish your current giveaway, bet, duel or heist first, then link.";

        keep.Aura += merge.Aura;
        keep.Lifetime += merge.Lifetime;
        if (merge.LastAccrualUtc > keep.LastAccrualUtc) keep.LastAccrualUtc = merge.LastAccrualUtc;

        foreach (string acc in merge.Accounts)
        {
            keep.Accounts.Add(acc);
            store.AccountToWallet[acc] = keep.Id;
        }

        // Carry goal contributions across
        long contributed;
        if (store.Goal != null && store.Goal.Contributors.TryGetValue(merge.Id, out contributed))
        {
            store.Goal.Contributors.Remove(merge.Id);
            long existing;
            store.Goal.Contributors.TryGetValue(keep.Id, out existing);
            store.Goal.Contributors[keep.Id] = existing + contributed;
        }

        store.Wallets.Remove(merge.Id);
        return $"linked! Combined balance: {Fmt(keep.Aura)} {CurrencyName}.";
    }

    private bool HasActiveStake(string walletId)
    {
        if (store.Giveaway != null && !store.Giveaway.Drawn && store.Giveaway.Tickets.ContainsKey(walletId)) return true;
        if (store.Prediction != null && store.Prediction.Bets.ContainsKey(walletId)) return true;
        if (heist != null && heist.Crew.ContainsKey(walletId)) return true;
        if (ring != null && ring.All.Any(p => p.WalletId == walletId)) return true;
        if (duels.ContainsKey(walletId) || duels.Values.Any(d => d.ChallengerId == walletId)) return true;
        return false;
    }

    private void UnlinkAccount(Ctx c)
    {
        if (c.Me.Accounts.Count <= 1) { Say(c, "this account isn't linked to anything."); return; }

        c.Me.Accounts.Remove(c.Key);
        Wallet fresh = NewWallet(c.Name);
        fresh.Accounts.Add(c.Key);
        store.AccountToWallet[c.Key] = fresh.Id;

        Say(c, "unlinked. Your balance stays with your other accounts, this one starts at 0.");
    }

    // ============================================================
    // GAMBLE
    // !gamble 500 | all | half | 2k
    // ============================================================

    private void Gamble(Ctx c)
    {
        if (c.Parts.Length < 1) { Say(c, $"usage: {CmdGamble} <amount|all|half>"); return; }

        DateTime now = DateTime.UtcNow;
        DateTime until;
        if (gambleCooldowns.TryGetValue(c.Me.Id, out until) && until > now)
        {
            Say(c, $"cooldown, try again in {Math.Ceiling((until - now).TotalSeconds)}s.");
            return;
        }

        long bet = Math.Min(ParseAmount(c.Parts[0], c.Me.Aura), GambleMax);
        if (bet < GambleMin) { Say(c, $"minimum bet is {Fmt(GambleMin)}."); return; }
        if (!TrySpend(c.Me, bet)) { Say(c, $"not enough, you have {Fmt(c.Me.Aura)}."); return; }

        gambleCooldowns[c.Me.Id] = now.AddSeconds(GambleCooldownSec);
        int roll = Rng.Next(1, 101);

        if (roll >= GambleWinRoll)
        {
            c.Me.Aura += bet * 2;
            Say(c, $"rolled {roll} and won {Fmt(bet)}! You now have {Fmt(c.Me.Aura)}.");
        }
        else
        {
            Say(c, $"rolled {roll} and lost {Fmt(bet)}. You now have {Fmt(c.Me.Aura)}.");
        }
    }

    // ============================================================
    // DUEL
    // !duel name 500  ->  target types !accept or !deny
    // ============================================================

    private void Duel(Ctx c)
    {
        if (c.Parts.Length < 2) { Say(c, $"usage: {CmdDuel} <name> <amount>"); return; }

        Wallet t = FindWalletByName(c.Platform, JoinParts(c.Parts, 0, c.Parts.Length - 1));
        long amount = ParseAmount(c.Parts.Last(), c.Me.Aura);

        if (t == null) { Say(c, "couldn't find that person."); return; }
        if (t.Id == c.Me.Id) { Say(c, "you can't duel yourself."); return; }
        if (amount < DuelMin) { Say(c, $"minimum duel is {Fmt(DuelMin)}."); return; }
        if (c.Me.Aura < amount) { Say(c, $"not enough, you have {Fmt(c.Me.Aura)}."); return; }
        if (t.Aura < amount) { Say(c, $"{t.Name} only has {Fmt(t.Aura)}."); return; }

        duels[t.Id] = new DuelRequest { ChallengerId = c.Me.Id, Amount = amount, ExpiresUtc = DateTime.UtcNow.AddSeconds(DuelTimeoutSec) };
        ReplyMany(PlatformsFor(c.Platform, t),
            $"@{t.Name} {c.Name} challenged you to a duel for {Fmt(amount)} {CurrencyName}! Type {CmdAccept} or {CmdDeny} within {DuelTimeoutSec}s.");
    }

    private void AcceptDuel(Ctx c)
    {
        DuelRequest d;
        if (!duels.TryGetValue(c.Me.Id, out d) || d.ExpiresUtc < DateTime.UtcNow)
        {
            duels.Remove(c.Me.Id);
            Say(c, "no duel waiting for you.");
            return;
        }
        duels.Remove(c.Me.Id);

        Wallet challenger;
        if (!store.Wallets.TryGetValue(d.ChallengerId, out challenger)) { Say(c, "that duel is no longer valid."); return; }
        if (challenger.Aura < d.Amount || c.Me.Aura < d.Amount) { Say(c, "someone can't cover the bet any more, duel cancelled."); return; }

        bool challengerWins = Rng.Next(2) == 0;
        Wallet winner = challengerWins ? challenger : c.Me;
        Wallet loser = challengerWins ? c.Me : challenger;

        loser.Aura -= d.Amount;
        winner.Aura += d.Amount;

        ReplyMany(PlatformsFor(c.Platform, challenger),
            $"{winner.Name} won the duel against {loser.Name} and took {Fmt(d.Amount)} {CurrencyName}!");
    }

    private void DenyDuel(Ctx c)
    {
        DuelRequest d;
        if (!duels.TryGetValue(c.Me.Id, out d)) { Say(c, "no duel waiting for you."); return; }
        duels.Remove(c.Me.Id);

        Wallet challenger;
        if (store.Wallets.TryGetValue(d.ChallengerId, out challenger))
            ReplyMany(PlatformsFor(c.Platform, challenger), $"{c.Name} declined the duel from {challenger.Name}.");
    }

    // ============================================================
    // HEIST
    // !heist 500  (first one starts it, others join within the window)
    // ============================================================

    private void Heist(Ctx c)
    {
        DateTime now = DateTime.UtcNow;

        if (heist == null && now < heistCooldownUntil)
        {
            Say(c, $"the crew is laying low, next heist in {Math.Ceiling((heistCooldownUntil - now).TotalSeconds)}s.");
            return;
        }
        if (c.Parts.Length < 1) { Say(c, $"usage: {CmdHeist} <amount>"); return; }
        if (heist != null && heist.Crew.ContainsKey(c.Me.Id)) { Say(c, "you're already in the crew."); return; }

        long stake = ParseAmount(c.Parts[0], c.Me.Aura);
        if (stake < HeistMin) { Say(c, $"minimum stake is {Fmt(HeistMin)}."); return; }
        if (!TrySpend(c.Me, stake)) { Say(c, $"not enough, you have {Fmt(c.Me.Aura)}."); return; }

        if (heist == null)
        {
            heist = new HeistState();
            heist.Crew[c.Me.Id] = stake;

            if (heistTimer != null) heistTimer.Dispose();
            heistTimer = new System.Threading.Timer(OnHeistTimer, null, (int)(HeistJoinSec * 1000), System.Threading.Timeout.Infinite);

            Announce($"{c.Name} is planning a heist! Type {CmdHeist} <amount> in the next {HeistJoinSec}s to join the crew.");
            return;
        }

        heist.Crew[c.Me.Id] = stake;
        Say(c, $"you're in! Crew size: {heist.Crew.Count}.");
    }

    private void OnHeistTimer(object state)
    {
        lock (Gate)
        {
            try { ResolveHeist(); Save(); }
            catch (Exception ex) { CPH.LogError("[EpicAura] Heist: " + ex); }
        }
    }

    private void ResolveHeist()
    {
        if (heist == null) return;

        var crew = heist.Crew;
        heist = null;
        heistCooldownUntil = DateTime.UtcNow.AddSeconds(HeistCooldownSec);

        int chance = Math.Min(HeistMaxChance, HeistBaseChance + HeistChancePerMember * (crew.Count - 1));
        var survivors = new List<string>();

        foreach (var kv in crew)
        {
            Wallet w;
            if (!store.Wallets.TryGetValue(kv.Key, out w)) continue;
            if (Rng.Next(100) >= chance) continue;

            long payout = (long)Math.Floor(kv.Value * HeistPayout);
            w.Aura += payout;
            survivors.Add($"{w.Name} (+{Fmt(payout - kv.Value)})");
        }

        if (survivors.Count == 0)
            Announce($"The heist went wrong! All {crew.Count} crew got caught and lost their {CurrencyName}.");
        else
            Announce($"Heist over! {survivors.Count}/{crew.Count} made it out: " + string.Join(", ", survivors));
    }

    // ============================================================
    // DROP (admin)
    // !drop 500  ->  first to type !claim gets it
    // ============================================================

    private void StartDrop(Ctx c)
    {
        long amount = c.Parts.Length > 0 ? ParseAmount(c.Parts[0], long.MaxValue) : -1;
        if (amount <= 0) { Say(c, $"usage: {CmdDrop} <amount>"); return; }

        drop = new DropState { Amount = amount, ExpiresUtc = DateTime.UtcNow.AddSeconds(DropClaimSec) };
        Announce($"{CurrencyName} drop! First to type {CmdClaim} gets {Fmt(amount)}!");
    }

    private void Claim(Ctx c)
    {
        if (drop == null) return;                                    // silent, avoids chat spam
        if (drop.ExpiresUtc < DateTime.UtcNow) { drop = null; return; }

        long amount = drop.Amount;
        drop = null;
        c.Me.Aura += amount;
        Announce($"{c.Name} grabbed the drop and got {Fmt(amount)} {CurrencyName}!");
    }

    // ============================================================
    // APEX FIGHT
    // !fight pathy 1v2 500   (any order, size defaults to 1v1, legend defaults to random)
    // ============================================================

    private void Fight(Ctx c)
    {
        if (c.Parts.Length == 0)
        {
            Say(c, $"usage: {CmdFight} <legend|random> <1v1|1v2|1v3> <amount>. 1v1 pays 2x, 1v2 pays 3x, 1v3 pays 5x.");
            return;
        }

        DateTime now = DateTime.UtcNow;
        DateTime until;
        if (fightCooldowns.TryGetValue(c.Me.Id, out until) && until > now)
        {
            Say(c, $"still healing up, fight again in {Math.Ceiling((until - now).TotalSeconds)}s.");
            return;
        }

        // Pull size and amount out, whatever is left is the legend
        string size = "1v1";
        string amountRaw = null;
        var legendWords = new List<string>();
        foreach (string p in c.Parts)
        {
            string l = p.ToLowerInvariant();
            if (FightSizes.ContainsKey(l)) size = l;
            else if (amountRaw == null && IsAmountToken(l)) amountRaw = l;
            else legendWords.Add(p);
        }

        string legend = ResolveLegend(string.Join(" ", legendWords));
        if (legend == null) { Say(c, Pick(UnknownLegendLines)); return; }
        if (amountRaw == null) { Say(c, $"how much? e.g. {CmdFight} {legend} {size} 500"); return; }

        long stake = Math.Min(ParseAmount(amountRaw, c.Me.Aura), FightMax);
        if (stake < FightMin) { Say(c, $"minimum fight is {Fmt(FightMin)}."); return; }
        if (!TrySpend(c.Me, stake)) { Say(c, $"not enough, you have {Fmt(c.Me.Aura)}."); return; }

        fightCooldowns[c.Me.Id] = now.AddSeconds(FightCooldownSec);
        FightOdds odds = FightSizes[size];
        bool won = Rng.Next(100) < odds.WinChance;

        if (won) FightWin(c, legend, size, stake, odds);
        else FightLoss(c, legend, size, stake);
    }

    private void FightWin(Ctx c, string legend, string size, long stake, FightOdds odds)
    {
        bool kraber = Rng.Next(100) < KraberChance;
        long payout = (long)Math.Floor(stake * odds.Payout * (kraber ? 2 : 1));
        c.Me.Aura += payout;

        string line;
        if (kraber) line = "KRABER HEADSHOT! {legend} one tapped the {size}. Double payout!";
        else if (size == "1v3" && Rng.Next(2) == 0) line = Pick(SquadWipeLines);
        else line = PickLegendLine(legend, LegendWinLines, FightWinLines);

        Say(c, $"{FillFight(line, legend, size)} (+{Fmt(payout - stake)}, now {Fmt(c.Me.Aura)})");

        // Streaks
        int streak;
        fightStreaks.TryGetValue(c.Me.Id, out streak);
        fightStreaks[c.Me.Id] = ++streak;

        if (streak >= KillLeaderStreak)
            Announce($"{c.Name} is the KILL LEADER with {streak} fight wins in a row! Somebody stop them!");
        else if (streak >= StreakShoutout)
            Reply(c.Platform, $"{c.Name} is on a {streak} fight win streak. Getting sweaty in here.");
        else if (size == "1v3")
            Announce($"{c.Name}'s {legend} just won a 1v3 for {Fmt(payout - stake)} {CurrencyName}!");
    }

    private void FightLoss(Ctx c, string legend, string size, long stake)
    {
        bool rezzed = Rng.Next(100) < RezChance;
        string line;

        if (rezzed)
        {
            long back = stake / 2;
            c.Me.Aura += back;
            line = "{legend} lost the {size} but a teammate pulled the banner. Half your Aura back.";
            Say(c, $"{FillFight(line, legend, size)} (-{Fmt(stake - back)}, now {Fmt(c.Me.Aura)})");
        }
        else
        {
            if (size == "1v1" && Rng.Next(3) == 0) line = Pick(OneVOneLossLines);
            else line = PickLegendLine(legend, LegendLossLines, FightLossLines);
            Say(c, $"{FillFight(line, legend, size)} (-{Fmt(stake)}, now {Fmt(c.Me.Aura)})");
        }

        // Streak broken
        int streak;
        if (fightStreaks.TryGetValue(c.Me.Id, out streak) && streak >= StreakShoutout)
            Reply(c.Platform, $"SHUTDOWN! {c.Name}'s {streak} win streak is over.");
        fightStreaks[c.Me.Id] = 0;
    }

    // Half the time use a legend specific line if there is one
    private string PickLegendLine(string legend, Dictionary<string, string[]> specific, string[] generic)
    {
        string[] lines;
        if (specific.TryGetValue(legend, out lines) && Rng.Next(2) == 0) return Pick(lines);
        return Pick(generic);
    }

    private string ResolveLegend(string raw)
    {
        if (legendLookup == null)
        {
            legendLookup = new Dictionary<string, string>();
            foreach (string entry in LegendList)
            {
                string[] names = entry.Split('|');
                foreach (string n in names) legendLookup[NormName(n)] = names[0];
            }
        }

        string key = NormName(raw);
        if (key == "" || key == "random")
        {
            string[] first = LegendList[Rng.Next(LegendList.Length)].Split('|');
            return first[0];
        }

        string legend;
        return legendLookup.TryGetValue(key, out legend) ? legend : null;
    }

    private static bool IsAmountToken(string s)
    {
        return s == "all" || s == "half" || ParseAmount(s, 1) > 0;
    }

    private static string FillFight(string line, string legend, string size)
    {
        return line.Replace("{legend}", legend).Replace("{size}", size);
    }

    private static string Pick(string[] options) { return options[Rng.Next(options.Length)]; }

    // ============================================================
    // RING (battle royale)
    // !ring 500 wraith   starts a lobby (legend optional, random if left out)
    // !ring octane       joins at the lobby's buy-in
    // !ring cancel       admin, refunds everyone
    // ============================================================

    private void Ring(Ctx c)
    {
        DateTime now = DateTime.UtcNow;
        string first = c.Parts.Length > 0 ? c.Parts[0].ToLowerInvariant() : "";

        if (first == "cancel") { if (c.Admin) CancelRing(); return; }
        if (ring != null && ring.Started) { Say(c, "the ring is already closing, catch the next one."); return; }
        if (ring != null && ring.All.Any(p => p.WalletId == c.Me.Id)) { Say(c, "you're already in the dropship."); return; }

        // Split out the buy-in, whatever is left is the legend
        string amountRaw = null;
        var legendWords = new List<string>();
        foreach (string p in c.Parts)
        {
            if (amountRaw == null && IsAmountToken(p.ToLowerInvariant())) amountRaw = p;
            else legendWords.Add(p);
        }

        string legend = ResolveLegend(string.Join(" ", legendWords));
        if (legend == null) { Say(c, Pick(UnknownLegendLines)); return; }

        // Start a new lobby
        if (ring == null)
        {
            if (now < ringCooldownUntil)
            {
                Say(c, $"the next ring opens in {Math.Ceiling((ringCooldownUntil - now).TotalSeconds)}s.");
                return;
            }
            if (amountRaw == null) { Say(c, $"usage: {CmdRing} <buy-in> [legend] to start a battle royale."); return; }

            long buyIn = Math.Min(ParseAmount(amountRaw, c.Me.Aura), RingMaxBuyIn);
            if (buyIn < RingMinBuyIn) { Say(c, $"minimum buy-in is {Fmt(RingMinBuyIn)}."); return; }
            if (!TrySpend(c.Me, buyIn)) { Say(c, $"not enough, you have {Fmt(c.Me.Aura)}."); return; }

            ring = new RingState { BuyIn = buyIn };
            AddRingPlayer(c, legend);

            if (ringTimer != null) ringTimer.Dispose();
            ringTimer = new System.Threading.Timer(OnRingTimer, null, (int)(RingJoinSec * 1000), (int)(RingRoundSec * 1000));

            Announce($"{c.Name} started a battle royale! Buy-in {Fmt(buyIn)} {CurrencyName}. Type {CmdRing} (add a legend if you want) in the next {RingJoinSec}s to drop in.");
            return;
        }

        // Join the open lobby
        if (!TrySpend(c.Me, ring.BuyIn)) { Say(c, $"buy-in is {Fmt(ring.BuyIn)}, you have {Fmt(c.Me.Aura)}."); return; }
        AddRingPlayer(c, legend);
        Say(c, $"your {legend} dropped in! {ring.All.Count} in the lobby, pot is {Fmt(RingPot())}.");
    }

    private void AddRingPlayer(Ctx c, string legend)
    {
        var p = new RingPlayer { WalletId = c.Me.Id, Name = c.Name, Legend = legend };
        ring.All.Add(p);
        ring.Alive.Add(p);
    }

    private void OnRingTimer(object state)
    {
        lock (Gate)
        {
            try { RingTick(); Save(); }
            catch (Exception ex) { CPH.LogError("[EpicAura] Ring: " + ex); }
        }
    }

    private void RingTick()
    {
        if (ring == null) { EndRing(false); return; }

        // First tick: lobby closes
        if (!ring.Started)
        {
            if (ring.All.Count < RingMinPlayers)
            {
                RefundRing();
                Announce($"Only {ring.All.Count} legend(s) dropped in, need {RingMinPlayers}. Buy-ins refunded.");
                EndRing(false);
                return;
            }

            ring.Started = true;
            Announce($"Dropship is empty! {ring.All.Count} legends, {Fmt(RingPot())} {CurrencyName} on the line. Ring 1 is closing...");
            return;
        }

        // Each tick after that: a ring closes
        ring.Round++;
        int alive = ring.Alive.Count;
        int cut = Math.Max(1, (int)Math.Ceiling(alive * RingEliminatePerRound));
        if (alive > 2) cut = Math.Min(cut, alive - 2);   // always leave a final 1v1
        else cut = 1;

        var knocked = new List<RingPlayer>();
        for (int i = 0; i < cut; i++)
        {
            int idx = Rng.Next(ring.Alive.Count);
            knocked.Add(ring.Alive[idx]);
            ring.Alive.RemoveAt(idx);
        }

        if (ring.Alive.Count == 1)
        {
            FinishRing(ring.Alive[0], knocked[0]);
            return;
        }

        string flavour = FillRing(Pick(RingElimLines), knocked[0]);
        string others = knocked.Count > 1 ? " Also out: " + string.Join(", ", knocked.Skip(1).Select(p => p.Name)) + "." : "";
        string next = ring.Alive.Count == 2 ? $"Final 1v1: {ring.Alive[0].Name} vs {ring.Alive[1].Name}!" : $"{ring.Alive.Count} left.";

        Announce($"RING {ring.Round} CLOSES! {flavour}{others} {next}");
    }

    private void FinishRing(RingPlayer champ, RingPlayer runnerUp)
    {
        long afterCut = RingPot() * (100 - RingHouseCutPercent) / 100;
        long runnerPrize = afterCut * RingRunnerUpPercent / 100;
        long champPrize = afterCut - runnerPrize;

        Wallet w;
        if (store.Wallets.TryGetValue(champ.WalletId, out w)) w.Aura += champPrize;
        if (store.Wallets.TryGetValue(runnerUp.WalletId, out w)) w.Aura += runnerPrize;

        Announce($"{champ.Name}'s {champ.Legend} won the final 1v1 and is the APEX CHAMPION! +{Fmt(champPrize)} {CurrencyName}. {runnerUp.Name} takes 2nd for {Fmt(runnerPrize)}.");
        EndRing(true);
    }

    private void CancelRing()
    {
        if (ring == null) return;
        RefundRing();
        Announce("Battle royale cancelled, buy-ins refunded.");
        EndRing(false);
    }

    private void RefundRing()
    {
        foreach (var p in ring.All)
        {
            Wallet w;
            if (store.Wallets.TryGetValue(p.WalletId, out w)) w.Aura += ring.BuyIn;
        }
    }

    private void EndRing(bool startCooldown)
    {
        ring = null;
        if (ringTimer != null) { ringTimer.Dispose(); ringTimer = null; }
        if (startCooldown) ringCooldownUntil = DateTime.UtcNow.AddSeconds(RingCooldownSec);
    }

    private long RingPot() { return ring.BuyIn * ring.All.Count; }

    private static string FillRing(string line, RingPlayer p)
    {
        return line.Replace("{name}", p.Name).Replace("{legend}", p.Legend);
    }

    // ============================================================
    // GIVEAWAY
    // !giveaway                                    status (anyone)
    // !giveaway start <ticketCost> <maxTickets> <prize>   cost 0 = free, 1 entry each
    // !giveaway close | draw | reroll | cancel | end
    // !enter [tickets]
    // ============================================================

    private void Giveaway(Ctx c)
    {
        string sub = c.Parts.Length > 0 ? c.Parts[0].ToLowerInvariant() : "";
        var g = store.Giveaway;

        if (!c.Admin || sub == "")
        {
            if (g == null) { Reply(c.Platform, "No giveaway running right now."); return; }
            Reply(c.Platform, GiveawayStatus(g));
            return;
        }

        switch (sub)
        {
            case "start":
                if (g != null) { Say(c, $"a giveaway is already running. Use {CmdGiveaway} end first."); return; }

                long cost; int max;
                if (c.Parts.Length < 4 || !long.TryParse(c.Parts[1], out cost) || !int.TryParse(c.Parts[2], out max) || cost < 0 || max < 1)
                {
                    Say(c, $"usage: {CmdGiveaway} start <ticketCost> <maxTickets> <prize>");
                    return;
                }
                if (cost == 0) max = 1;   // free giveaway: one entry each

                store.Giveaway = new GiveawayState { Prize = JoinParts(c.Parts, 3, c.Parts.Length - 3), TicketCost = cost, MaxTickets = max, Open = true };
                Announce("Giveaway open! " + GiveawayStatus(store.Giveaway));
                break;

            case "close":
                if (g == null) return;
                g.Open = false;
                Announce($"Giveaway entries closed. {GiveawayCounts(g)}");
                break;

            case "draw":
            case "reroll":
                if (g == null) return;
                g.Open = false;
                g.Drawn = true;
                DrawWinner(g);
                break;

            case "cancel":
                if (g == null) return;
                if (!g.Drawn)
                {
                    foreach (var kv in g.Tickets)
                    {
                        Wallet w;
                        if (store.Wallets.TryGetValue(kv.Key, out w)) w.Aura += kv.Value * g.TicketCost;
                    }
                }
                store.Giveaway = null;
                Announce("Giveaway cancelled, tickets refunded.");
                break;

            case "end":
                store.Giveaway = null;
                Say(c, "giveaway cleared.");
                break;
        }
    }

    private void EnterGiveaway(Ctx c)
    {
        var g = store.Giveaway;
        if (g == null || !g.Open) { Say(c, "no giveaway open right now."); return; }

        int have;
        g.Tickets.TryGetValue(c.Me.Id, out have);
        int canBuy = g.MaxTickets - have;
        if (canBuy <= 0) { Say(c, $"you already have the max {g.MaxTickets} ticket(s)."); return; }

        int want = 1;
        if (c.Parts.Length > 0)
        {
            if (c.Parts[0].ToLowerInvariant() == "max") want = canBuy;
            else if (!int.TryParse(c.Parts[0], out want) || want < 1) want = 1;
        }
        want = Math.Min(want, canBuy);

        // Buy as many as they can afford
        if (g.TicketCost > 0) want = (int)Math.Min(want, c.Me.Aura / g.TicketCost);
        if (want <= 0) { Say(c, $"tickets cost {Fmt(g.TicketCost)}, you have {Fmt(c.Me.Aura)}."); return; }

        TrySpend(c.Me, want * g.TicketCost);
        g.Tickets[c.Me.Id] = have + want;
        Say(c, $"you have {have + want} ticket(s) for {g.Prize}. Good luck!");
    }

    private void DrawWinner(GiveawayState g)
    {
        var pool = g.Tickets.Where(kv => !g.Winners.Contains(kv.Key) && store.Wallets.ContainsKey(kv.Key)).ToList();
        int total = pool.Sum(kv => kv.Value);
        if (total == 0) { Announce("No eligible entries left to draw from."); return; }

        int pick = Rng.Next(total);
        foreach (var kv in pool)
        {
            pick -= kv.Value;
            if (pick >= 0) continue;

            Wallet w = store.Wallets[kv.Key];
            g.Winners.Add(w.Id);
            string platforms = string.Join(", ", w.Accounts.Select(a => Cap(PlatformOfKey(a))).Distinct());
            Announce($"The winner of {g.Prize} is {w.Name} ({platforms}) with {kv.Value} ticket(s)! Congrats!");
            return;
        }
    }

    private string GiveawayStatus(GiveawayState g)
    {
        string price = g.TicketCost == 0 ? "Free entry" : $"{Fmt(g.TicketCost)} per ticket, max {g.MaxTickets}";
        string state = g.Open ? $"Type {CmdEnter} to join." : "Entries closed.";
        return $"Prize: {g.Prize}. {price}. {state} {GiveawayCounts(g)}";
    }

    private static string GiveawayCounts(GiveawayState g)
    {
        return $"{g.Tickets.Values.Sum()} tickets from {g.Tickets.Count} people.";
    }

    // ============================================================
    // PREDICTIONS (great for Apex matches)
    // !predict                                         status (anyone)
    // !predict open Will we win? | Win | Top 5 | Neither
    // !predict lock | result <n> | cancel
    // !bet <option number> <amount>
    // ============================================================

    private void Predict(Ctx c)
    {
        string sub = c.Parts.Length > 0 ? c.Parts[0].ToLowerInvariant() : "";
        var p = store.Prediction;

        if (!c.Admin || sub == "")
        {
            if (p == null) { Reply(c.Platform, "No prediction running right now."); return; }
            Reply(c.Platform, PredictionStatus(p));
            return;
        }

        switch (sub)
        {
            case "open":
                if (p != null) { Say(c, $"a prediction is already running. Use {CmdPredict} result or cancel first."); return; }

                var bits = JoinParts(c.Parts, 1, c.Parts.Length - 1).Split('|').Select(s => s.Trim()).Where(s => s != "").ToList();
                if (bits.Count < 3) { Say(c, $"usage: {CmdPredict} open <question> | <option 1> | <option 2> ..."); return; }

                store.Prediction = new PredictionState { Question = bits[0], Options = bits.Skip(1).ToList() };
                Announce("Prediction open! " + PredictionStatus(store.Prediction));
                break;

            case "lock":
                if (p == null) return;
                p.Locked = true;
                Announce($"Prediction locked! Total pool: {Fmt(p.Bets.Values.Sum(b => b.Amount))} {CurrencyName}.");
                break;

            case "result":
                int option;
                if (p == null || c.Parts.Length < 2 || !int.TryParse(c.Parts[1], out option) || option < 1 || option > p.Options.Count)
                {
                    Say(c, $"usage: {CmdPredict} result <option number>");
                    return;
                }
                PayOutPrediction(p, option);
                store.Prediction = null;
                break;

            case "cancel":
                if (p == null) return;
                foreach (var kv in p.Bets)
                {
                    Wallet w;
                    if (store.Wallets.TryGetValue(kv.Key, out w)) w.Aura += kv.Value.Amount;
                }
                store.Prediction = null;
                Announce("Prediction cancelled, all bets refunded.");
                break;
        }
    }

    private void Bet(Ctx c)
    {
        var p = store.Prediction;
        if (p == null) { Say(c, "no prediction running right now."); return; }
        if (p.Locked) { Say(c, "betting is locked."); return; }

        int option;
        if (c.Parts.Length < 2 || !int.TryParse(c.Parts[0], out option) || option < 1 || option > p.Options.Count)
        {
            Say(c, $"usage: {CmdBet} <1-{p.Options.Count}> <amount>");
            return;
        }

        PredictionBet existing;
        p.Bets.TryGetValue(c.Me.Id, out existing);
        if (existing != null && existing.Option != option)
        {
            Say(c, $"you already backed {p.Options[existing.Option - 1]}, you can only add to that.");
            return;
        }

        long amount = ParseAmount(c.Parts[1], c.Me.Aura);
        if (amount <= 0 || !TrySpend(c.Me, amount)) { Say(c, $"not enough, you have {Fmt(c.Me.Aura)}."); return; }

        if (existing == null) p.Bets[c.Me.Id] = existing = new PredictionBet { Option = option };
        existing.Amount += amount;

        Say(c, $"{Fmt(existing.Amount)} on {p.Options[option - 1]}.");
    }

    // Winners split the whole pool in proportion to what they bet
    private void PayOutPrediction(PredictionState p, int option)
    {
        long total = p.Bets.Values.Sum(b => b.Amount);
        var winners = p.Bets.Where(kv => kv.Value.Option == option).ToList();
        long winPool = winners.Sum(kv => kv.Value.Amount);
        string result = p.Options[option - 1];

        if (winPool == 0)
        {
            Announce($"Result: {result}. Nobody backed it, the {Fmt(total)} {CurrencyName} pool is gone!");
            return;
        }

        var paid = new List<KeyValuePair<string, long>>();
        foreach (var kv in winners)
        {
            Wallet w;
            if (!store.Wallets.TryGetValue(kv.Key, out w)) continue;
            long payout = (long)Math.Floor((double)kv.Value.Amount * total / winPool);
            w.Aura += payout;
            paid.Add(new KeyValuePair<string, long>(w.Name, payout));
        }

        var top = paid.OrderByDescending(x => x.Value).Take(3).Select(x => $"{x.Key} (+{Fmt(x.Value)})");
        Announce($"Result: {result}! {winners.Count} winner(s) split {Fmt(total)}. Top: " + string.Join(", ", top));
    }

    private string PredictionStatus(PredictionState p)
    {
        var opts = p.Options.Select((o, i) =>
        {
            long pool = p.Bets.Values.Where(b => b.Option == i + 1).Sum(b => b.Amount);
            return $"{i + 1}) {o} [{Fmt(pool)}]";
        });
        string how = p.Locked ? "Locked." : $"Type {CmdBet} <number> <amount>";
        return $"{p.Question} " + string.Join(" ", opts) + ". " + how;
    }

    // ============================================================
    // COMMUNITY GOAL (pooled across all platforms)
    // !goal                                  status (anyone)
    // !goal start <target> <reward>          e.g. !goal start 100000 Cosplay Stream
    // !goal cancel (refunds) | end (clears, no refund)
    // !contribute <amount>
    // ============================================================

    private void Goal(Ctx c)
    {
        string sub = c.Parts.Length > 0 ? c.Parts[0].ToLowerInvariant() : "";
        var g = store.Goal;

        if (!c.Admin || sub == "")
        {
            if (g == null) { Reply(c.Platform, "No community goal running right now."); return; }
            Reply(c.Platform, GoalStatus(g));
            return;
        }

        switch (sub)
        {
            case "start":
                long target;
                if (g != null) { Say(c, $"a goal is already running. Use {CmdGoal} end or cancel first."); return; }
                if (c.Parts.Length < 3 || (target = ParseAmount(c.Parts[1], long.MaxValue)) <= 0)
                {
                    Say(c, $"usage: {CmdGoal} start <target> <reward>");
                    return;
                }
                store.Goal = new GoalState { Target = target, Reward = JoinParts(c.Parts, 2, c.Parts.Length - 2) };
                Announce("New community goal! " + GoalStatus(store.Goal));
                break;

            case "cancel":
                if (g == null) return;
                foreach (var kv in g.Contributors)
                {
                    Wallet w;
                    if (store.Wallets.TryGetValue(kv.Key, out w)) w.Aura += kv.Value;
                }
                store.Goal = null;
                Announce("Community goal cancelled, contributions refunded.");
                break;

            case "end":
                store.Goal = null;
                Say(c, "goal cleared.");
                break;
        }
    }

    private void Contribute(Ctx c)
    {
        var g = store.Goal;
        if (g == null) { Say(c, "no community goal running right now."); return; }
        if (c.Parts.Length < 1) { Say(c, $"usage: {CmdContribute} <amount>"); return; }

        long amount = Math.Min(ParseAmount(c.Parts[0], c.Me.Aura), g.Target - g.Progress);   // never overshoot
        if (amount <= 0 || !TrySpend(c.Me, amount)) { Say(c, $"not enough, you have {Fmt(c.Me.Aura)}."); return; }

        long before;
        g.Contributors.TryGetValue(c.Me.Id, out before);
        g.Contributors[c.Me.Id] = before + amount;
        g.Progress += amount;

        if (g.Progress < g.Target)
        {
            Say(c, $"added {Fmt(amount)}. " + GoalStatus(g));
            return;
        }

        var top = g.Contributors.OrderByDescending(kv => kv.Value).Take(3)
            .Where(kv => store.Wallets.ContainsKey(kv.Key))
            .Select(kv => $"{store.Wallets[kv.Key].Name} ({Fmt(kv.Value)})");
        Announce($"GOAL REACHED! {g.Reward} is unlocked! Top contributors: " + string.Join(", ", top));
        store.Goal = null;
    }

    private string GoalStatus(GoalState g)
    {
        double pct = g.Target > 0 ? (double)g.Progress / g.Target * 100 : 0;
        return $"{g.Reward}: {Fmt(g.Progress)} / {Fmt(g.Target)} ({pct:0}%). Type {CmdContribute} <amount> to help.";
    }

    // ============================================================
    // ADMIN
    // ============================================================

    // !addaura name 500   (negative to remove)
    private void AddAura(Ctx c)
    {
        long amount;
        if (c.Parts.Length < 2 || !long.TryParse(c.Parts.Last(), out amount)) { Say(c, $"usage: {CmdAdd} <name> <amount>"); return; }

        Wallet t = FindWalletByName(c.Platform, JoinParts(c.Parts, 0, c.Parts.Length - 1));
        if (t == null) { Say(c, "couldn't find that person."); return; }

        t.Aura = Math.Max(0, t.Aura + amount);
        if (amount > 0) t.Lifetime += amount;

        Reply(c.Platform, $"{t.Name} now has {Fmt(t.Aura)} {CurrencyName}.");
    }

    // !aurareset confirm
    private void ResetAll(Ctx c)
    {
        if (c.Parts.Length == 0 || c.Parts[0].ToLowerInvariant() != "confirm")
        {
            Say(c, $"this wipes everyone's balance. Type {CmdReset} confirm to do it.");
            return;
        }

        Backup("pre-reset");
        foreach (var w in store.Wallets.Values) { w.Aura = 0; w.Carry = 0; }
        Announce($"All {CurrencyName} balances have been reset to 0.");
    }

    // ============================================================
    // WALLETS
    // ============================================================

    private Wallet GetOrCreateWallet(string platform, string userId, string name)
    {
        string key = AccountKey(platform, userId);
        if (!string.IsNullOrEmpty(name)) store.AccountNames[key] = name;

        string walletId;
        if (store.AccountToWallet.TryGetValue(key, out walletId) && store.Wallets.ContainsKey(walletId))
        {
            Wallet existing = store.Wallets[walletId];
            if (!string.IsNullOrEmpty(name)) existing.Name = name;
            return existing;
        }

        Wallet w = NewWallet(name);
        w.Accounts.Add(key);
        store.AccountToWallet[key] = w.Id;
        return w;
    }

    private Wallet NewWallet(string name)
    {
        var w = new Wallet { Id = Guid.NewGuid().ToString("N"), Name = name, LastAccrualUtc = DateTime.MinValue };
        store.Wallets[w.Id] = w;
        return w;
    }

    // Same platform first, then any platform
    private Wallet FindWalletByName(string platform, string rawName)
    {
        string norm = NormName(rawName);
        var matches = store.AccountNames.Where(kv => NormName(kv.Value) == norm).Select(kv => kv.Key).ToList();
        string key = matches.FirstOrDefault(k => PlatformOfKey(k) == platform) ?? matches.FirstOrDefault();
        if (key == null) return null;

        string walletId;
        return store.AccountToWallet.TryGetValue(key, out walletId) && store.Wallets.ContainsKey(walletId) ? store.Wallets[walletId] : null;
    }

    private static bool TrySpend(Wallet w, long amount)
    {
        if (amount < 0 || w.Aura < amount) return false;
        w.Aura -= amount;
        return true;
    }

    // Accepts 500, 2k, 1.5k, all, half
    private static long ParseAmount(string raw, long balance)
    {
        string s = (raw ?? "").Trim().ToLowerInvariant().Replace(",", "");
        if (s == "all") return balance;
        if (s == "half") return balance / 2;

        double mult = 1;
        if (s.EndsWith("k")) { mult = 1000; s = s.Substring(0, s.Length - 1); }

        double d;
        if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d) || d <= 0) return -1;
        return (long)Math.Floor(d * mult);
    }

    private void PurgeExpiredLinks()
    {
        DateTime now = DateTime.UtcNow;
        foreach (string k in linkRequests.Where(kv => kv.Value.ExpiresUtc < now).Select(kv => kv.Key).ToList())
            linkRequests.Remove(k);
    }

    // ============================================================
    // STORAGE
    // ============================================================

    private void EnsureLoaded()
    {
        if (store != null) return;
        Directory.CreateDirectory(DataDir);

        if (File.Exists(DataPath))
        {
            // If the file is corrupt, try the .bak. If both fail, throw so real data is never overwritten with an empty store.
            try { store = JsonConvert.DeserializeObject<Store>(File.ReadAllText(DataPath)); }
            catch
            {
                CPH.LogWarn("[EpicAura] Main file unreadable, trying backup");
                store = JsonConvert.DeserializeObject<Store>(File.ReadAllText(DataPath + ".bak"));
            }
            Backup("startup");
        }

        if (store == null) store = new Store();
        if (store.Wallets == null) store.Wallets = new Dictionary<string, Wallet>();
        if (store.AccountToWallet == null) store.AccountToWallet = new Dictionary<string, string>();
        if (store.AccountNames == null) store.AccountNames = new Dictionary<string, string>();
        foreach (var w in store.Wallets.Values) if (w.Accounts == null) w.Accounts = new List<string>();

        CPH.LogInfo($"[EpicAura] Loaded {store.Wallets.Count} wallets");
    }

    private void Save()
    {
        string tmp = DataPath + ".tmp";
        File.WriteAllText(tmp, JsonConvert.SerializeObject(store, Formatting.Indented));

        if (File.Exists(DataPath)) File.Replace(tmp, DataPath, DataPath + ".bak");
        else File.Move(tmp, DataPath);
    }

    private void Backup(string tag)
    {
        if (!File.Exists(DataPath)) return;
        string file = Path.Combine(DataDir, $"epicaura-{tag}-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        File.Copy(DataPath, file, true);
    }

    // ============================================================
    // CHAT
    // ============================================================

    private void Say(Ctx c, string msg) { Reply(c.Platform, $"@{c.Name} {msg}"); }

    private void Reply(string platform, string msg)
    {
        int max = platform == "youtube" ? YouTubeMaxChars : OtherMaxChars;
        if (msg.Length > max) msg = msg.Substring(0, max - 3) + "...";

        if (platform == "youtube") CPH.SendYouTubeMessage(msg);
        else if (platform == "kick") CPH.SendKickMessage(msg);
        else CPH.SendMessage(msg);
    }

    private void ReplyMany(IEnumerable<string> platforms, string msg)
    {
        foreach (string p in platforms)
        {
            try { Reply(p, msg); }
            catch (Exception ex) { CPH.LogWarn($"[EpicAura] Send to {p} failed: {ex.Message}"); }
        }
    }

    private void Announce(string msg) { ReplyMany(AnnouncePlatforms, msg); }

    // Current platform plus every platform the wallets have accounts on
    private static IEnumerable<string> PlatformsFor(string current, params Wallet[] wallets)
    {
        var set = new List<string> { current };
        foreach (var w in wallets)
            foreach (var a in w.Accounts) set.Add(PlatformOfKey(a));
        return set.Distinct().ToList();   // no duplicate messages
    }

    private bool IsAdmin(string key)
    {
        if (AdminIds.Contains(key)) return true;
        return ModsAreAdmins && (ArgBool("isModerator") || ArgBool("isBroadcaster"));
    }

    // ============================================================
    // HELPERS
    // ============================================================

    private string EventName()
    {
        string src = Arg("__source");
        return !string.IsNullOrEmpty(src) ? src : CPH.GetEventType().ToString();
    }

    private static string PlatformFrom(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        string t = text.ToLowerInvariant();
        if (t.Contains("youtube") || t == "yt") return "youtube";
        if (t.Contains("kick")) return "kick";
        if (t.Contains("twitch")) return "twitch";
        return null;
    }

    private static string AccountKey(string platform, string userId) { return platform + ":" + userId; }
    private static string PlatformOfKey(string key) { return key.Substring(0, key.IndexOf(':')); }
    private static string CleanName(string n) { return (n ?? "").Trim().TrimStart('@'); }
    private static string NormName(string n) { return CleanName(n).Replace(" ", "").ToLowerInvariant(); }
    private static string Cap(string s) { return s == "youtube" ? "YouTube" : char.ToUpper(s[0]) + s.Substring(1); }
    private static string Fmt(long n) { return n.ToString("N0"); }

    private static string JoinParts(string[] parts, int start, int count)
    {
        return string.Join(" ", parts.Skip(start).Take(Math.Max(0, count)));
    }

    private static string FirstNonEmpty(params string[] values)
    {
        return values.FirstOrDefault(v => !string.IsNullOrEmpty(v)) ?? "";
    }

    private bool IsIgnored(string name, string login)
    {
        return IgnoredNames.Contains(NormName(name)) || IgnoredNames.Contains(NormName(login));
    }

    private string Arg(string key)
    {
        object o;
        return args.TryGetValue(key, out o) && o != null ? o.ToString() : "";
    }

    private bool ArgBool(string key)
    {
        bool b;
        return bool.TryParse(Arg(key), out b) && b;
    }

    private static string Str(Dictionary<string, object> d, params string[] keys)
    {
        foreach (string k in keys)
        {
            object o;
            if (d.TryGetValue(k, out o) && o != null && o.ToString() != "") return o.ToString();
        }
        return "";
    }

    private static bool Bool(Dictionary<string, object> d, params string[] keys)
    {
        foreach (string k in keys)
        {
            object o;
            bool b;
            if (d.TryGetValue(k, out o) && o != null && bool.TryParse(o.ToString(), out b) && b) return true;
        }
        return false;
    }

    // Logs the field names Streamer.bot sends per platform, once, so sub detection can be checked
    private void LogUserKeysOnce(string platform, Dictionary<string, object> sample)
    {
        if (loggedKeysFor.Contains(platform)) return;
        loggedKeysFor.Add(platform);
        CPH.LogInfo($"[EpicAura] {platform} present viewer fields: " + string.Join(", ", sample.Keys));
    }
}
