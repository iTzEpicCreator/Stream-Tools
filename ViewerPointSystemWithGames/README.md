# Epic Aura - Streamer.bot Points System

A cross-platform points currency for Twitch, YouTube and Kick, with giveaways, predictions, community goals, a timed welcome message and Apex Legends themed chat games. One action, one command, one timer, one C# file. Needs no extra references.

For viewers: share `VIEWER_GUIDE.md`. It explains how to earn and spend Epic Aura without any setup details, and it's the link `!aurahelp` sends in chat (`HelpUrl` in the code).

## Install (first time, about 5 minutes)

Make sure Twitch, YouTube and Kick are connected in Streamer.bot first (Platforms tab).

### 1. Create the action
1. **Actions** tab, right click the actions list, **Add**, name it `Epic Aura - Core`.
2. In the **Sub-Actions** panel, right click, **Core > C# > Execute C# Code**.
3. Delete the template code, paste in all of `EpicAura.cs`, click **Compile**. The log at the bottom should say it compiled successfully.
   - If it ever complains about Newtonsoft: open the **References** tab, right click, **Add reference from file**, pick `Newtonsoft.Json.dll` in your Streamer.bot folder, compile again.
4. Click **Save and Compile**.

### 2. Add the earning triggers
In the **Triggers** panel of the same action, right click and add all three:
- **Twitch > General > Present Viewers**
- **YouTube > General > Present Viewers**
- **Kick > General > Present Viewers**

### 3. Create the command
1. **Commands** tab, right click, **Add**.
2. Name: `Epic Aura Commands`
3. Commands box: paste the whole list below (one per line).
4. Location: **Start**. Case sensitive: off.
5. Sources: tick **Twitch Message**, **YouTube Message** and **Kick Message**.
6. Global cooldown and user cooldown: **0** (the script handles its own cooldowns).
7. Make sure it's **Enabled**, then click OK.

```
!aura
!points
!auratop
!stats
!gametop
!give
!link
!unlink
!auraid
!aurahelp
!aurainfo
!gamble
!duel
!accept
!deny
!heist
!claim
!fight
!ring
!giveaway
!enter
!predict
!bet
!goal
!contribute
!addaura
!takeaura
!aurareset
!drop
!rain
!giveall
```

8. Back on the `Epic Aura - Core` action, Triggers panel, right click, **Core > Commands > Command Triggered**, pick `Epic Aura Commands`.

### 4. Create the welcome message timer
1. **Settings > Timers**, right click, **Add**.
2. Name `Epic Aura Promo`, tick **Enabled** and **Repeat**.
3. Interval `1800` (every 30 mins). Or tick **Random** and use `1500` to `2100` for 25 to 35 mins.
4. Lines `0` (Lines doesn't work with multiple platforms connected).
5. On the action, Triggers panel, right click, **Core > Timed Actions**, pick `Epic Aura Promo`.

It only posts while you're live (it checks viewers were seen in the last 15 mins).

### 5. Make yourself admin
1. Go live (or have the platforms connected) and type `!auraid` on Twitch, YouTube and Kick.
2. Paste each result into `AdminIds` near the top of the code (all three platforms), e.g.
   ```
   "twitch:12345678", "youtube:UCxxxxxxxx", "kick:1234567"
   ```
3. Compile again. Mods also count as admins while `ModsAreAdmins` is `true`.

### 6. First stream checks
- **Earning:** after a few minutes, `!aura` should show a balance. Earning starts from the second Present Viewers tick, so the first few minutes pay nothing.
- **Sub detection:** in the Streamer.bot log, look for `[EpicAura] twitch present viewer fields:` (and youtube/kick). This lists what Streamer.bot tells the script about each viewer. If none of `subscribed`, `isSubscribed`, `isSponsor`, `isMember` appear, subs earn the normal rate instead of 1.5x until the field name is added to the code.
- **Replies:** type `!aura` on each platform and check the bot replies on all three, especially Kick.
- **Promo:** type `!aurainfo` to test the welcome message on every platform.

### 7. Export it (for backup and sharing)
1. Top bar, **Export**.
2. Drag in the `Epic Aura - Core` action, the `Epic Aura Commands` command and the `Epic Aura Promo` timer.
3. Fill in name, description and author, then copy the string.
4. Save it as `EpicAura.sb.txt` next to this README. Remove your `AdminIds` from the shared copy.

## Quick install (from an export string)

1. Copy the string from `EpicAura.sb.txt`.
2. Streamer.bot top bar, **Import**, paste, **Import**.
3. Open the `Epic Aura - Core` action, double click the Execute C# Code sub-action, **Compile**.
4. Do step 5 (make yourself admin) above.

## Viewer commands

| Command | What it does |
|---|---|
| `!aura [name]` / `!points` | Check a balance |
| `!auratop` / `!auratop earned` | Top 5 richest now / most earned all time |
| `!stats [name]` | Game record: fights, best streak, champion wins, duels, heists, biggest win |
| `!gametop <category>` | Game leaderboards: `fights`, `streak`, `kraber`, `ring`, `duels`, `heist`, `gamble`, `bigwin` |
| `!aurahelp` | Lists the main commands |
| `!give <name> <amount>` | Send Aura to someone |
| `!link <platform> <name>` then `!link confirm` | Merge balances across platforms |
| `!unlink` | Split this account back out (starts at 0) |
| `!gamble <amount>` | 45% chance to double, 30s cooldown |
| `!duel <name> <amount>`, `!accept`, `!deny` | 50/50, winner takes the bet |
| `!heist <amount>` | Group heist, 60s to join, more crew = better odds |
| `!fight <legend> <1v1/1v2/1v3> <amount>` | Apex fight, pays 2x / 3x / 5x |
| `!ring <buy-in> [legend]` | Battle royale, last one standing wins the pot |
| `!enter [tickets / max]` | Enter a giveaway |
| `!bet <option> <amount>` | Bet on a prediction |
| `!contribute <amount>` | Add to the community goal |
| `!claim` | Grab a drop |
| `!giveaway`, `!predict`, `!goal` | Show what's currently running |

Amounts accept numbers, `all`, `half` and `2k` style.

## Admin commands

| Command | Example |
|---|---|
| Giveaway | `!giveaway start 100 5 1000 Apex Coins` (cost per ticket, max tickets each, prize). Cost `0` = free, one entry each. Then `close`, `draw`, `reroll`, `cancel` (refunds), `end` |
| Prediction | `!predict open Will we win? \| Win \| Top 5 \| Neither`, then `lock`, `result 2`, `cancel` (refunds) |
| Community goal | `!goal start 100k Cosplay Stream`, then `cancel` (refunds) or `end` |
| Drop | `!drop 500` (first to `!claim` in 60s gets it) |
| Rain | `!rain 500` or `!giveall 500` gives everyone present 500 each (seen by a viewer tick or used a command in the last 15 mins) |
| Welcome message | `!aurainfo` posts the next one on every platform, `!aurainfo 3` posts a specific one |
| Balances | `!addaura <name> <amount>` adds, `!takeaura <name> <amount>` removes (never below 0) |
| Resets | `!aurareset warn this Sunday` warns viewers a reset is coming. `!aurareset` (new season: balances, earned and stats), `!aurareset balances` or `!aurareset stats` shows a warning, then `!aurareset confirm` within 60s does it. With `SeasonsEnabled = true` in the code, the full reset also posts final standings and starts a numbered season (off by default). A backup is always saved, and it won't run while a giveaway, prediction, goal, heist or ring is active. |
| Ring | `!ring cancel` (refunds) |

## Settings

Everything adjustable is in the CONFIG blocks at the top of the code: currency name, earn rate, sub multiplier, game odds and cooldowns, ring and fight settings, legend list, welcome messages (`PromoLines`), `HelpUrl`, command names and all the chat lines. Compile again after any change.

## Notes

- Data saves to `data\EpicAura\epicaura.json` in the Streamer.bot folder, with a `.bak` on every save and timestamped backups on startup and before resets. Don't edit the JSON while Streamer.bot is running.
- YouTube and Kick viewers only earn while chatting (Streamer.bot can't see lurkers there). Twitch lurkers earn.
- Heists, rings, duels and drops live in memory. If Streamer.bot restarts mid-game, those stakes are lost.
- Keep chat lines under 200 characters, YouTube cuts off anything longer.
- Points have no cash value. If you run giveaways with real prizes, keep a free entry route (ticket cost 0).

Made by iTzTerryEpic
