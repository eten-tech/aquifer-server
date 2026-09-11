# AI translation workflow

This describes how resource content gets machine-translated (or, for
Aquiferization, text-improved) via OpenAI, from the triggering API request
through to the OpenAI chat completion call itself.

## Entry points (Aquifer.API)

A user (or automated project flow) triggers translation by publishing a queue
message via `ITranslationMessagePublisher` (`Aquifer.Common`):

| Endpoint | Message published | Scope |
|---|---|---|
| `Resources/Content/CreateTranslation` | `TranslateResourceMessage` | Single resource content item |
| `Resources/Content/Aquiferize` | `TranslateResourceMessage` (Aquiferization origin) | Single resource content item, text-improvement only |
| `Projects/Start` | `TranslateProjectResourcesMessage` | All resource contents in a project (fan-out/fan-in) |
| (parent-resource language rollout) | `TranslateLanguageResourcesMessage` | All resources under a parent resource, into a new target language |

## Queue consumption and orchestration (Aquifer.Jobs)

`TranslationMessageSubscriber`
(`src/Aquifer.Jobs/Subscribers/TranslationMessageSubscriber.cs`) consumes
these messages:

- **`TranslateResourceMessageSubscriber`** — translates one resource content
  item directly.
- **`TranslateProjectResourcesMessageSubscriber`** — starts a Durable
  Functions orchestration (`OrchestrateProjectResourcesTranslationAsync`)
  that fans out one `TranslateResourceActivity` per resource in the project,
  waits for all to complete, then assigns every translated resource to the
  project's company lead and sends a "project started" notification.
- **`TranslateLanguageResourcesMessageSubscriber`** — starts a similar
  orchestration (`OrchestrateLanguageResourcesTranslationAsync`) that, per
  source resource content version, first creates the target-language
  `ResourceContent`/`ResourceContentVersion` rows
  (`CreateLanguageResourceContentActivity`) if they don't already exist, then
  translates and auto-publishes them.

Failures in either orchestration are swallowed after the durable retry policy
(5 retries, 1s backoff) is exhausted, and the original queue message is
republished to a poison queue for manual replay.

All paths converge on **`TranslateResourceCoreAsync`**
(`TranslationMessageSubscriber.cs`), which:

1. Loads the draft `ResourceContentVersion` and its target `Language`.
2. Builds a `translationPairs` glossary dictionary from the `TranslationPairs`
   table for the target language (skipped for Aquiferization or non-English
   sources).
3. Converts the stored Tiptap JSON content to HTML chunks
   (`TiptapConverter.ConvertJsonToHtmlItems`) — some resource types (e.g.
   FIA) have multiple steps, each translated independently.
4. Calls `ITranslationService.TranslateTextAsync` for the display name and
   `TranslateHtmlAsync` for each HTML content step — this is where OpenAI is
   invoked.
5. Records each translated HTML step as a
   `ResourceContentVersionMachineTranslationEntity`
   (`SourceId = MachineTranslationSourceId.OpenAi`).
6. Runs language-specific post-processing via `ITranslationProcessingService`
   (e.g. `ChineseTranslationProcessingService`,
   `FrenchTranslationProcessingService`,
   `PortugueseTranslationProcessingService`).
7. Updates word count, content, snapshots, and resource status; for the
   `Language` origin (fully automated rollout), also publishes the resource
   content version.

## The OpenAI call (Aquifer.AI)

`OpenAiTranslationService` (`src/Aquifer.AI/OpenAiTranslationService.cs`)
implements `ITranslationService`:

- **`TranslateTextAsync`** — used for plain text such as display names.
- **`TranslateHtmlAsync`** — splits HTML into paragraphs (split on `<h1-6>`/
  `<p>` boundaries), translates paragraphs in parallel batches (max 3
  concurrent), and for each paragraph runs a second "text improvement" pass
  on the translated result. When `shouldOnlyPerformTextImprovement` is true
  (Aquiferization), the translation pass is skipped and only the improvement
  pass runs.

Both funnel through `CompleteChatAsync`, which calls
`ChatClient.CompleteChatAsync(messages, ChatCompletionOptions, ct)` with a
system message (the prompt) and a user message (the text/HTML to process).

## Where the OpenAI request parameters come from

| Parameter | Set in | Source |
|---|---|---|
| Model (e.g. `gpt-4o`) | `OpenAiTranslationService` constructor — `new ChatClient(openAiOptions.Model, ...)` | `OpenAiOptions.Model`, `appsettings.json` → `OpenAi.Model` |
| API key | Same constructor — fetched from Key Vault secret `OpenAiApiKey` | Azure Key Vault, via `IAzureKeyVaultClient` |
| Network timeout | Same constructor — `s_openAiNetworkTimeout` | Hardcoded constant (10 minutes) |
| Temperature | `CreateChatCompletionOptions` — set only if `SupportsTemperature` is true | `OpenAiTranslationOptions.Temperature` (appsettings, e.g. `0.2`), gated by `OpenAiOptions.SupportsTemperature` (false for reasoning models such as GPT-5/o-series, which reject it) |
| Reasoning effort | `CreateChatCompletionOptions` — set only if non-null | `OpenAiOptions.ReasoningEffortLevel` (appsettings) |
| Max output tokens | `CreateChatCompletionOptions` — set only if non-null | `OpenAiOptions.MaxOutputTokens` (appsettings) |
| System prompt | `GetHtmlTranslationPrompt`, `GetHtmlTextImprovementPrompt`, `GetPlainTextTranslationPrompt` — format target language name into a template, plus a per-language appendix for the improvement pass | `OpenAiTranslationOptions`: `HtmlBasePrompt`, `TranslationPromptFormatString`, `TextImprovementPromptFormatString`, `PlainTextTranslationPromptFormatString`, `LanguageSpecificTextImprovementPromptAppendixByLanguageIso6393CodeMap` (all in `appsettings.json`) |
| User message content | `TranslateResourceCoreAsync` chunks Tiptap JSON to HTML per paragraph; `MaskTranslationPairs` masks glossary terms behind placeholder tokens before sending | Source `ResourceContentVersion.Content`, `TranslationPairs` DB table |
| Translate vs. improve-only | `TranslateHtmlAsync`'s `shouldOnlyPerformTextImprovement` flag | `isAquiferization` in `TranslateResourceCoreAsync`, based on `ResourceContent.Status` |

Configuration-driven parameters (model, temperature, reasoning effort, max
tokens, prompt templates) live in `src/Aquifer.Jobs/appsettings.json` under
the `OpenAi` and `OpenAiTranslation` sections, bound via
`Aquifer.Jobs.Configuration.ConfigurationOptions` and registered as
singletons in `Program.cs`. Code-driven parameters (which prompt applies,
what content is sent, whether to translate or just improve) are decided in
`OpenAiTranslationService.cs` and `TranslationMessageSubscriber.cs`.

## Where the prompt is defined

The system prompt text itself is configuration, not code — it lives in
`src/Aquifer.Jobs/appsettings.json` under `OpenAiTranslation`:

- **`HtmlBasePrompt`** — base instructions for preserving HTML structure
  (never alter tags/attributes, never add missing elements, never remove
  `&nbsp;`). Prepended to every HTML prompt.
- **`TranslationPromptFormatString`** — `"Then translate to {0}. Never echo
  the original content, only respond with the translation."` `{0}` is the
  target language's English display name.
- **`TextImprovementPromptFormatString`** — `"Improve the grammar and
  clarity of the provided {0} text. Never echo the original content, only
  respond with the alteration."`
- **`PlainTextTranslationPromptFormatString`** — equivalent prompt used for
  plain text (e.g. display names) instead of HTML.
- **`LanguageSpecificTextImprovementPromptAppendixByLanguageIso6393CodeMap`**
  — a per-language map of extra rules appended only during the text
  improvement pass (e.g. French punctuation/guillemets, Chinese full-width
  punctuation, Hindi honorifics, Portuguese Bible-book abbreviations).
  Keyed by ISO 639-3 code.

These templates are assembled into the final system message string by
`OpenAiTranslationService.cs` (`src/Aquifer.AI/OpenAiTranslationService.cs`):

- **`GetHtmlTranslationPrompt`** — `HtmlBasePrompt` + `TranslationPromptFormatString`
  (formatted with the target language name).
- **`GetHtmlTextImprovementPrompt`** — `HtmlBasePrompt` +
  `TextImprovementPromptFormatString` + the language-specific appendix (if
  one exists for the target language).
- **`GetPlainTextTranslationPrompt`** — just `PlainTextTranslationPromptFormatString`
  formatted with the target language name.

The resulting string is passed into `CompleteChatAsync` as `prompt` and sent
to OpenAI as the system message via `ChatMessage.CreateSystemMessage(prompt)`,
paired with the content to translate/improve as the user message.

### Translation pair handling (placeholder masking)

Translation pairs are no longer substituted into the text as literal
target-language values before calling OpenAI. Instead,
`OpenAiTranslationService.MaskTranslationPairs` replaces each matched
glossary term with an opaque placeholder token (e.g. `⟦AQP0⟧`) before any
OpenAI call. The masked text goes through both the translation pass and the
text-improvement pass; `UnmaskTranslationPairs` then replaces each
placeholder with its real glossary value once both passes are complete,
before the chunk is returned to the caller. This guarantees the human
translator's approved term survives the "improve grammar and clarity" pass
intact, rather than relying on the model happening not to touch it — a
model-agnostic guarantee where the earlier substitution approach depended
on model behavior that changed between `gpt-4o` and newer reasoning models.
The system prompts (`OpenAiTranslationService.PlaceholderPreservationInstruction`)
also explicitly instruct the model not to alter placeholder tokens, as
defense in depth; if the model still drops or mangles one, a warning is
logged (see `UnmaskTranslationPairs`).

If the full text being translated exactly matches a translation pair key
(`TryGetFullTranslationPairReplacement`), OpenAI is never called at all —
the pair's value is returned directly, as before.
