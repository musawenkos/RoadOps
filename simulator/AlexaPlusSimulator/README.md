# RoadOps Alexa+ simulator

The RoadOps voice agent, run in a simulated Alexa+ device. Real Alexa+ Add-ons are partner-only, so this stands in for
it: the [mcp-voice-simulator](https://github.com/musawenkos/mcp-voice-simulator) provides the device-style web UI, the
MCP client and MCP Apps views, and its **Bedrock brain** plays the part Alexa's own LLM plays in Alexa+: it takes the
inspector's utterance, calls the RoadOps MCP tools in a loop on Amazon Bedrock, and answers in short, speakable
language.

What this app adds on top of the generic simulator:

| File | Purpose |
|---|---|
| `prompts/roadops-system.md` | The RoadOps system prompt: speech style, the read-back and confirm flow for writes, GPS for "here", honesty rules. Edit it freely; it's read at startup. |
| `src/device.ts` | What the phone knows (GPS fix, recently uploaded photos, inspector name, local time), sent to the agent as context with every turn. |
| `src/field-brain.ts` | Typed stand-ins for the phone's GPS and camera (`/gps`, `/upload`, ...), loading the previous session's summary, minimal per-turn logging, and which RoadOps tools need a confirmation turn. |
| `src/config.ts` | Settings from environment variables. |
| `src/main.ts` | Starts the simulator with all of the above. |

## Memory across sessions

Voice sessions drop, and inspectors come back the next day. The RoadOps server keeps one summary per inspector
(`get_session_summary` / `save_session_summary`, table `inspector_sessions`):

- **Position:** the survey, section and km the inspector saved, or the place of their latest observation when that is
  newer. So a session that dropped without saving still resumes at the right place.
- **What's ahead:** how many poor observations (degree 4-5) the previous survey of the corridor had from there to the
  end of the section.
- **Follow-ups:** what the inspector asked to remember, in their words (stored as data, quoted when read back).

On the first turn of each session (startup, or after **Reset**), this app calls `get_session_summary` and passes the
result to the agent as `previousSession`, so "Alexa, let's continue" gets an answer straight away. The agent saves when
the inspector stops, takes a break or asks it to remember something; saving needs no confirmation turn, since it only
changes the inspector's own bookmark.

## Safety limits

- **At most 8 tool calls per turn** (`SIM_MAX_STEPS`); then the agent answers with what it has.
- **No write without a confirmation turn**, enforced in code, not only in the prompt. The brain holds any call to
  `update_observation` or `void_observation` and only makes it when the identical call comes on the inspector's next
  turn. `log_observation` previews first (no `confirm`); the `confirm=true` call only runs on a later turn with the same
  values. `attach_photo` runs straight away (uploading a photo is already a deliberate act), and so does
  `save_session_summary` (it changes no survey data). The MCP server also enforces
  its own rules (roles, own observations only, 10-minute void window).
- **Identity comes from the API key** on the MCP connection. The `inspector` name in the context is only for addressing
  the inspector; no tool accepts it.
- **The console log has tool names and timings only**, never arguments or results, which can hold notes and locations.
  The browser trace has full results, because MCP Apps views need them; it stays on 127.0.0.1.

## Run it

Prerequisites: Node 20+, the RoadOps MCP server running, an inspector API key with the `editor` role, and an AWS
profile with Bedrock access (ours is `roadops-bedrock`).

```powershell
# Once: install this app (from the RoadOps root)
cd simulator\AlexaPlusSimulator
npm install
```

The simulator itself is the [Bedrock brain branch of our mcp-voice-simulator fork](https://github.com/musawenkos/mcp-voice-simulator/tree/aa2b0421ed0839ae18fc7e6eaf027d064f5fb2fb),
pinned to commit `aa2b042`. It's vendored as a built package in `vendor/mcp-voice-simulator-0.2.2-aa2b042.tgz`, so
`npm install` needs neither git nor a build step, and the version can't change. To pin another commit, run
`./scripts/pack-voice-simulator.ps1 -Commit <sha>`, update the file name in `package.json`, and run `npm install`.

```powershell
# Each time: start PostgreSQL and the MCP server (from the RoadOps root)...
docker compose up -d
dotnet run --project src/RoadOps.Mcp --launch-profile http

# ...then, in another terminal, this app
cd simulator\AlexaPlusSimulator
$env:MCP_BEARER_TOKEN = "<the inspector's MCP API key>"   # the key itself, not its hash
$env:AWS_PROFILE = "<your AWS profile with Bedrock access>"
$env:SIM_GPS = "-25.3951, 28.2799"   # optional: a starting position on the N1
npm start
```

Open the printed `http://127.0.0.1:8790` and type utterances.

| Env var | Purpose |
|---|---|
| `MCP_BEARER_TOKEN` | **required**: the inspector's RoadOps MCP API key. Never put it in a file in the repo. |
| `MCP_URL` | default `http://localhost:5288/mcp` |
| `AWS_PROFILE` | the AWS profile with Bedrock access, e.g. `roadops-bedrock`. Credentials always come from the AWS SDK's own chain. |
| `AWS_REGION` | default `us-east-1` |
| `SIM_BEDROCK_MODEL` | default `us.anthropic.claude-haiku-4-5-20251001-v1:0` (Claude Haiku 4.5), the model the manual tests ran on. Any Bedrock model with tool use through the Converse API should work, e.g. a Sonnet or Opus model if your account can call it. Check a model with `scripts/bedrock-smoke.ts <model-id>` first: on our account newer Claude models failed with an AWS Marketplace `AccessDeniedException` ([friction log 6](../../docs/friction-log.md#6-a-bedrock-model-that-worked-stopped-working-with-an-aws-marketplace-error)). |
| `SIM_MAX_STEPS` | most tool calls per turn, 1-20 (default 8) |
| `SIM_GPS` | starting GPS position, `latitude, longitude` |
| `INSPECTOR_NAME` | optional, for the assistant to address the inspector |
| `SIM_SYSTEM_PROMPT_FILE` | use another prompt file instead of `prompts/roadops-system.md` |
| `SIM_PORT`, `SIM_UI` | as in the simulator: page port (default 8790), MCP Apps views `on`/`off` |

### Typed commands

Until the page captures GPS and photos itself, type these in the simulator. They are handled locally and never reach
the model:

| Command | Does |
|---|---|
| `/gps -25.3951, 28.2799` | sets the phone's position ("here"). `/gps off` clears it. |
| `/upload C:\photos\pothole.jpg` | uploads a photo to `POST /uploads/photos` and makes it the latest photo |
| `/photo <id>` | makes an already uploaded photo the latest one |
| `/device` | shows the context the agent gets each turn |

## Tests

```bash
npm test             # unit tests, no AWS or MCP calls
npm run typecheck
```

Manual checks against real Bedrock and the running MCP server: [MANUAL-TESTS.md](MANUAL-TESTS.md).
`scripts/bedrock-smoke.ts` is a quick check that a model works with the brain, needing neither the database nor the
MCP server (a few cheap calls):

```powershell
$env:AWS_PROFILE = "<your AWS profile>"; npx tsx scripts/bedrock-smoke.ts   # the default model; or pass a model id
```
