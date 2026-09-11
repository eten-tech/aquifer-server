# Plan: make translation pairs respected by reasoning models

Status: context/problem statement — implementation plan TBD.

See [ai-translation-workflow.md](../../ai-translation-workflow.md) for the full
translation workflow and prompt reference this builds on.

## Problem

Translation pairs are a glossary of human-vetted term overrides, defined by
a human translator who is treated as the authority on correct terminology
for a target language (`TranslationPairs` table, keyed by `LanguageId`).
They exist to force specific terms (e.g. names, theological/technical
vocabulary) to a known-correct rendering rather than trusting the model's
own translation of that term.

With `gpt-4o`, translation pairs were effectively respected. After switching
to a newer reasoning model (`gpt5.6-luna`), translation pairs are no longer
being respected — the model appears to override the glossary term with its
own translation.

## Root cause

Translation pairs are **not communicated to the model as an instruction at
all**. The mechanism is purely positional/pre-processing, in
`src/Aquifer.AI/OpenAiTranslationService.cs`:

1. `ReplaceTranslationPairs` (`:182-195`) runs *before* any OpenAI call. It
   regex-substitutes each glossary term directly into the source text/HTML,
   producing a string that mixes source-language and target-language text
   (e.g. an English sentence with the approved target-language term dropped
   in place of the English term).
2. That mixed-language string is sent to OpenAI with the translation prompt
   (`GetHtmlTranslationPrompt` → `"Then translate to {0}..."`).
3. The result is immediately passed through a **second** call, the "text
   improvement" pass (`GetHtmlTextImprovementPrompt` →
   `"Improve the grammar and clarity of the provided {0} text..."`).

Neither prompt ever mentions that some terms in the text are pre-translated
glossary entries that must be preserved exactly. The whole scheme relies on
the model simply not touching text that already looks like it's in the
target language — an implicit assumption, not an enforced constraint.

This assumption held (mostly by accident) with `gpt-4o`, which tended to be
more literal/passive and less likely to "fix" an oddity it noticed in the
text. A stronger reasoning model is more capable of noticing that a
glossary-substituted term reads awkwardly in context (inconsistent
register, unexpected collocation, etc.) during the "improve grammar and
clarity" pass, and retranslates it to something more fluent — discarding
the human translator's authoritative term in the process. Reasoning-model
behavior differences (`OpenAiOptions.ReasoningEffortLevel`,
`SupportsTemperature`) plausibly compound this: reasoning models are more
inclined toward normalizing/rewriting text they're asked to "improve"
rather than passing suspicious-looking fragments through unchanged.

## Goal

Every translation pair substitution present going into a paragraph/chunk
must still be present, unchanged, in that chunk's final output — after both
the translation pass and the text-improvement pass — regardless of which
OpenAI model is configured. The fix should not depend on a particular
model's tendency to leave things alone; the model must be constrained to
keep the human translator's exact term through any "improve" processing,
not merely encouraged to.

## Constraints / things to preserve

- Translation pairs are keyed per target language and are authored by a
  human translator considered authoritative — the fix must not give the
  model discretion to "correct" a pair's value.
- The two-pass pipeline (translate, then improve grammar/clarity) and the
  per-language improvement appendices
  (`LanguageSpecificTextImprovementPromptAppendixByLanguageIso6393CodeMap`)
  should keep working for the rest of the text.
- Must work across resource types translated via `TranslateHtmlAsync`
  (paragraph-chunked HTML) and `TranslateTextAsync` (plain text, e.g.
  display names).
- Should not depend on `SupportsTemperature`/`ReasoningEffortLevel` being
  set a particular way, since those vary by configured model
  (`appsettings.json` → `OpenAi`).

## Open questions for the implementation plan

- Do we make the constraint explicit in the prompt (tell the model which
  terms are fixed and must not be altered), do we protect substituted
  terms from the "improve" pass entirely (e.g. placeholder/mask-and-restore
  around `ReplaceTranslationPairs` output), or both?
- If we mask terms instead of substituting real target-language text, do we
  still substitute the final glossary value after both OpenAI passes
  complete, removing any risk of the model touching it?
- Do we need per-pair guarantees (e.g. verify post-hoc that each expected
  substitution is still present in the model output) with a retry/alert
  path if a pair was clobbered?
