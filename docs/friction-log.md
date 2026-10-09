# Friction log

Problems hit while building RoadOps for the Amazon Developer Hackathon (Alexa+ track, AWS Builder and Open Source
mini challenges) with Alexa+, Amazon Bedrock, MCP and the community simulator. Newest last.

Severity: **blocker** (no way forward without a workaround), **high** (costs hours or changes the design),
**medium** (costs time, easy once known), **low** (annoyance).

---

## 1. Alexa+ MCP Toolkit and Add-ons are partner-only

- **Date:** 2026-09 (project start)
- **Area:** Alexa+
- **Task:** Connect the RoadOps MCP server to Alexa+ and test it with real utterances.
- **Steps:** Followed the Alexa+ Add-ons docs to register an add-on and find the MCP Toolkit / `alexa-ai` CLI.
- **Expected:** A way for hackathon participants to register an MCP server and test it on a device or a developer
  simulator.
- **Actual:** The docs say the Category SDK and MCP Toolkit are available to select partners only. There is no public
  CLI, registration or simulator.
- **Severity:** blocker
- **Workaround:** Simulate Alexa+ in a web app, as the rules allow: the community
  [mcp-voice-simulator](https://github.com/AlSayedGamal/mcp-voice-simulator) as the device and MCP client, with our own
  Bedrock brain in place of Alexa's LLM.
- **Suggestion:** Give hackathon participants time-limited access to the MCP Toolkit, or publish a developer simulator
  that takes an MCP server URL.

## 2. Bedrock lists Claude Sonnet 5 but returns AccessDenied

- **Date:** 2026-10-08
- **Area:** Amazon Bedrock
- **Task:** Pick the model for the agent.
- **Steps:** Listed the models and inference profiles available to the account in `us-east-1`, then called
  `us.anthropic.claude-sonnet-5` with the Converse API using the `roadops-bedrock` profile.
- **Expected:** A model listed for the account can be invoked, or the listing says it needs an access request.
- **Actual:** `AccessDeniedException`. The same IAM user can call Claude Sonnet 4.5, Haiku 4.5 and Opus 4.5.
- **Severity:** medium
- **Workaround:** Default to `us.anthropic.claude-sonnet-4-5-20250929-v1:0`. Added `scripts/bedrock-smoke.ts` to the
  simulator app to try a model with the agent before switching, and noted it in the simulator fork's README.
- **Suggestion:** Show access status next to each listed model (or leave out models the account can't call), and say in
  the error what is missing: model access, a marketplace subscription, or a quota.

## 3. Two different AWS credit form links

- **Date:** 2026-10
- **Area:** Hackathon site
- **Task:** Request the $150 AWS credits.
- **Steps:** Compared the credit request link on the rules page with the one on the resources page.
- **Expected:** One form.
- **Actual:** The two pages link to different forms, with no note on which one is current.
- **Severity:** low
- **Workaround:** Used one form; the credits were applied. Set a budget alert.
- **Suggestion:** Link one form from both pages, or say which one replaces the other.

## 4. The simulator's `npm run check` fails on a fresh Windows clone

- **Date:** 2026-10-08
- **Area:** mcp-voice-simulator
- **Task:** Run the fork's checks before adding the Bedrock brain.
- **Steps:** `git clone` on Windows (Git's default `core.autocrlf=true`), `npm install --legacy-peer-deps`,
  `npm run check`.
- **Expected:** All checks pass on an untouched clone.
- **Actual:** `verify:host` reports `public/host.js is stale`: Git checked the generated file out with CRLF line endings,
  and the check compares it byte for byte with a fresh LF build. Typecheck and all tests pass.
- **Severity:** low
- **Workaround:** Run `npm run typecheck` and `npm test` separately on Windows; CI (Linux) is unaffected.
- **Suggestion:** Add a `.gitattributes` with `public/host.js text eol=lf` (or `* text=auto eol=lf`), or have the check
  ignore line-ending differences. A small upstream PR.

## 5. The simulator's Brain interface had no way to pass device context

- **Date:** 2026-10-08
- **Area:** mcp-voice-simulator
- **Task:** Let the agent resolve "log a crack **here**" and "attach **this** photo".
- **Steps:** Read the `Brain` interface: `turn(utterance)` returns `{ reply, trace }`.
- **Expected:** Some way to give the agent what the device knows (GPS fix, an uploaded photo id), as Alexa+ does with
  device context.
- **Actual:** Only the utterance reaches the brain.
- **Severity:** medium
- **Workaround:** Extended the fork, generically: `turn(utterance, context?)`, a `turnContext` option on `createServer`
  and an optional `context` object on `POST /api/turn`. The built-in brains pass it to the model labelled as coming
  from the app, not the user. Proposed upstream with the Bedrock brain.
- **Suggestion:** None for upstream beyond accepting the change; for Alexa+, document what device context an add-on's
  tools can receive.

## 6. A Bedrock model that worked stopped working with an AWS Marketplace error

- **Date:** 2026-10-09
- **Area:** Amazon Bedrock
- **Task:** Run the manual agent checks on Claude Sonnet 4.5 (`us.anthropic.claude-sonnet-4-5-20250929-v1:0`), the
  model the agent had used the day before.
- **Steps:** Started the simulator with the `roadops-bedrock` profile (IAM user with Bedrock invoke permissions) and
  asked "Where am I?". Then ran `scripts/bedrock-smoke.ts` with the same model id.
- **Expected:** The same answers as on 2026-10-08, when Converse calls to this model succeeded with the same user,
  profile and region.
- **Actual:** `AccessDeniedException`: the IAM user "is not authorized to perform the required AWS Marketplace actions
  (aws-marketplace:ViewSubscriptions, aws-marketplace:Subscribe) to enable access to this model". Claude Haiku 4.5
  (`us.` and `global.` profiles) still worked with the same user. Claude Sonnet 4.6, never used before, answered one
  request and refused the next minute with the same error, so a model seems to work only until its automatic
  Marketplace subscription fails. Newer models (Haiku 5.5, Sonnet 5.5, Opus 4.8) are listed in the console and as
  inference profiles, but return "not available for this account". The same day, AWS announced that Sonnet 4.5 had
  entered the Legacy state (end of life 2027-04-08), which is a separate thing from this error.
- **Severity:** high
- **Workaround:** Made Claude Haiku 4.5 the app's default model. Any other model can be chosen with
  `SIM_BEDROCK_MODEL` once the account can subscribe to it: an administrator grants the Marketplace subscribe
  actions, or the account's billing setup allows Marketplace purchases.
- **Suggestion:** Bedrock should say up front which models need a one-time Marketplace subscription, and explain why
  access that worked can be refused later. The error could name who has to act, the user or an account admin.

## 7. Converse can end a turn with no text after a successful tool call

- **Date:** 2026-10-09
- **Area:** Amazon Bedrock (Claude Haiku 4.5 through the Converse API)
- **Task:** Run the session memory checks: "The culvert's done, clear my reminders."
- **Steps:** The agent called `save_session_summary`, which succeeded, and its tool result went back to the model.
- **Expected:** A short spoken confirmation such as "Cleared."
- **Actual:** The model ended the turn (`end_turn`) with no text block, so the brain fell back to its generic reply
  "Sorry, I don't have an answer for that.", which sounds like a failure. It happened on 2 of 3 saves in one session,
  and not on the other write tools.
- **Severity:** medium
- **Workaround:** Fixed in the fork's Bedrock brain: when a turn ends with no text after tool calls, it asks the model
  once for a one-sentence reply, and if that is empty too, says "Done." when every call succeeded (the generic apology
  only follows a failed call). Checked again on Haiku 4.5: the same requests got spoken replies.
- **Suggestion:** Document that an assistant turn after `toolResult` can be empty, and how clients should handle it.
