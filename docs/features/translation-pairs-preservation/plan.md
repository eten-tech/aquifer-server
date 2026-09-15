# Translation Pairs Placeholder Masking Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Guarantee that every translation-pair term substituted into a chunk of text going into OpenAI is still present, unchanged, in that chunk's final translated/improved output, regardless of which OpenAI model is configured.

**Architecture:** Replace the current "substitute the real glossary value into the source text and hope the model leaves it alone" approach with mask-and-restore: before any OpenAI call, swap each matched glossary term for an opaque placeholder token (e.g. `⟦AQP0⟧`). Send the masked text through both the translation pass and the text-improvement pass. After both passes complete, do a literal string replace of each placeholder back to its real glossary value. The model is only ever asked to leave an inert token untouched — never asked to recognize and preserve arbitrary foreign-language text — which is a far more reliable instruction for any model, including reasoning models. As defense in depth, also add an explicit instruction to the system prompts telling the model not to alter placeholder tokens, and log a warning if a placeholder goes missing from a model response (so drift is observable even in the rare case restoration can't find it).

**Tech Stack:** C# / .NET 9, OpenAI .NET SDK (`OpenAI.Chat.ChatClient`), xUnit (existing test project, no mocking library available — tests use real instances with fake/null dependencies).

**Spec:** [design.md](design.md)

## Global Constraints

- Every translation-pair substitution present going into a paragraph/chunk must survive both the translation pass and the text-improvement pass unchanged, regardless of configured model.
- Must not depend on `OpenAiOptions.SupportsTemperature` / `ReasoningEffortLevel` being set any particular way.
- Must keep working for both `TranslateTextAsync` (plain text, e.g. display names) and `TranslateHtmlAsync` (paragraph-chunked HTML).
- Must not break the existing full-text-exact-match shortcut in `TranslateTextAsync`, which bypasses OpenAI entirely.
- Must keep the two-pass HTML pipeline (translate, then improve grammar/clarity) and the per-language improvement appendices working for the rest of the text.
- No mocking library is available in `tests/Aquifer.Jobs.UnitTests` — new logic must be testable via plain xUnit facts against `internal static` methods or real instances constructed with fake/null dependencies (mirroring the existing `CreateChatCompletionOptions` test pattern, enabled by `Aquifer.AI`'s existing `InternalsVisibleTo` for `Aquifer.Jobs.UnitTests`).

---

## File Structure

| File | Responsibility |
|---|---|
| `src/Aquifer.AI/OpenAiTranslationService.cs` | All changes live here: masking/unmasking helpers replace `ReplaceTranslationPairs`; prompt builders get a placeholder-preservation instruction appended; `TranslateTextAsync`/`TranslateHtmlAsync` are rewired to mask before calling OpenAI and unmask after; constructor gains an `ILogger<OpenAiTranslationService>` dependency. |
| `tests/Aquifer.Jobs.UnitTests/Services/OpenAiTranslationServiceTests.cs` | New test cases for the masking/unmasking helpers and the prompt builders. |
| `docs/ai-translation-workflow.md` | Update the "translation pairs" and "prompt" descriptions to reflect the new placeholder mechanism. |

No new files are needed — this is a contained, single-file logic change plus its tests and docs.

---

### Task 1: Replace `ReplaceTranslationPairs` with placeholder mask/unmask helpers

**Files:**
- Modify: `src/Aquifer.AI/OpenAiTranslationService.cs`
- Test: `tests/Aquifer.Jobs.UnitTests/Services/OpenAiTranslationServiceTests.cs`

**Interfaces:**
- Produces (used by Task 3):
  - `internal static string? TryGetFullTranslationPairReplacement(string text, IDictionary<string, string> translationPairs)`
  - `internal static (string MaskedText, IReadOnlyDictionary<string, string> PlaceholderValueMap) MaskTranslationPairs(string text, IDictionary<string, string> translationPairs)`
  - `internal static string UnmaskTranslationPairs(string text, IReadOnlyDictionary<string, string> placeholderValueMap, ILogger logger)`
  - `private const string PlaceholderTokenPrefix = "⟦AQP";` / `private const string PlaceholderTokenSuffix = "⟧";` (e.g. `⟦AQP0⟧`)

- [ ] **Step 1: Write the failing tests**

Add to `tests/Aquifer.Jobs.UnitTests/Services/OpenAiTranslationServiceTests.cs` (add `using Microsoft.Extensions.Logging.Abstractions;` and `using System.Collections.Generic;` if not already present via implicit usings — this project already has implicit usings enabled via the SDK default, so only add `Microsoft.Extensions.Logging.Abstractions`):

```csharp
[Fact]
public void TryGetFullTranslationPairReplacement_WhenTextExactlyMatchesPairKey_ReturnsPairValue()
{
    var translationPairs = new Dictionary<string, string> { ["Yahweh"] = "YHWH-translated" };

    var result = OpenAiTranslationService.TryGetFullTranslationPairReplacement("yahweh", translationPairs);

    Assert.Equal("YHWH-translated", result);
}

[Fact]
public void TryGetFullTranslationPairReplacement_WhenTextDoesNotMatchAnyPairKey_ReturnsNull()
{
    var translationPairs = new Dictionary<string, string> { ["Yahweh"] = "YHWH-translated" };

    var result = OpenAiTranslationService.TryGetFullTranslationPairReplacement("Yahweh is great", translationPairs);

    Assert.Null(result);
}

[Fact]
public void MaskTranslationPairs_WhenTextContainsPairKey_ReplacesItWithAPlaceholderAndReturnsTheMap()
{
    var translationPairs = new Dictionary<string, string> { ["Yahweh"] = "YHWH-translated" };

    var (maskedText, placeholderValueMap) = OpenAiTranslationService.MaskTranslationPairs("Yahweh is great", translationPairs);

    Assert.DoesNotContain("Yahweh", maskedText);
    Assert.Single(placeholderValueMap);
    var placeholder = Assert.Single(placeholderValueMap.Keys);
    Assert.Contains(placeholder, maskedText);
    Assert.Equal("YHWH-translated", placeholderValueMap[placeholder]);
}

[Fact]
public void MaskTranslationPairs_WhenTextDoesNotContainAnyPairKey_ReturnsOriginalTextAndAnEmptyMap()
{
    var translationPairs = new Dictionary<string, string> { ["Yahweh"] = "YHWH-translated" };

    var (maskedText, placeholderValueMap) = OpenAiTranslationService.MaskTranslationPairs("The Lord is great", translationPairs);

    Assert.Equal("The Lord is great", maskedText);
    Assert.Empty(placeholderValueMap);
}

[Fact]
public void MaskTranslationPairs_WhenMultipleKeysMatch_MasksBothIndependently()
{
    var translationPairs = new Dictionary<string, string>
    {
        ["Yahweh"] = "YHWH-translated",
        ["Moses"] = "Moses-translated",
    };

    var (maskedText, placeholderValueMap) = OpenAiTranslationService.MaskTranslationPairs("Yahweh spoke to Moses", translationPairs);

    Assert.DoesNotContain("Yahweh", maskedText);
    Assert.DoesNotContain("Moses", maskedText);
    Assert.Equal(2, placeholderValueMap.Count);
}

[Fact]
public void UnmaskTranslationPairs_WhenPlaceholderIsPresent_RestoresThePairValue()
{
    var placeholderValueMap = new Dictionary<string, string> { ["⟦AQP0⟧"] = "YHWH-translated" };

    var result = OpenAiTranslationService.UnmaskTranslationPairs(
        "⟦AQP0⟧ is great",
        placeholderValueMap,
        NullLogger.Instance);

    Assert.Equal("YHWH-translated is great", result);
}

[Fact]
public void UnmaskTranslationPairs_WhenPlaceholderIsMissing_LeavesTextUnchanged()
{
    var placeholderValueMap = new Dictionary<string, string> { ["⟦AQP0⟧"] = "YHWH-translated" };

    var result = OpenAiTranslationService.UnmaskTranslationPairs(
        "The text no longer has the token",
        placeholderValueMap,
        NullLogger.Instance);

    Assert.Equal("The text no longer has the token", result);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Aquifer.Jobs.UnitTests --filter "FullyQualifiedName~OpenAiTranslationServiceTests"`
Expected: FAIL to compile — `TryGetFullTranslationPairReplacement`, `MaskTranslationPairs`, and `UnmaskTranslationPairs` don't exist yet.

- [ ] **Step 3: Replace `ReplaceTranslationPairs` with the new helpers**

In `src/Aquifer.AI/OpenAiTranslationService.cs`, add these two constants near the top of the class (alongside `MaxContentLength`/`MaxParallelizationForSingleTranslation`):

```csharp
private const string PlaceholderTokenPrefix = "⟦AQP";
private const string PlaceholderTokenSuffix = "⟧";
```

Delete the existing `ReplaceTranslationPairs` method:

```csharp
private static (string Text, bool IsFullReplace) ReplaceTranslationPairs(string text, IDictionary<string, string> translationPairs)
{
    foreach (var pair in translationPairs.OrderByDescending(x => x.Key.Length))
    {
        if (string.Equals(pair.Key, text, StringComparison.InvariantCultureIgnoreCase))
        {
            return (pair.Value, true);
        }

        text = Regex.Replace(text, $"""\b(?:{pair.Key})\b""", pair.Value, RegexOptions.IgnoreCase);
    }

    return (text, false);
}
```

Replace it with:

```csharp
internal static string? TryGetFullTranslationPairReplacement(string text, IDictionary<string, string> translationPairs)
{
    foreach (var pair in translationPairs)
    {
        if (string.Equals(pair.Key, text, StringComparison.InvariantCultureIgnoreCase))
        {
            return pair.Value;
        }
    }

    return null;
}

internal static (string MaskedText, IReadOnlyDictionary<string, string> PlaceholderValueMap) MaskTranslationPairs(
    string text,
    IDictionary<string, string> translationPairs)
{
    var placeholderValueMap = new Dictionary<string, string>();
    var index = 0;

    foreach (var pair in translationPairs.OrderByDescending(x => x.Key.Length))
    {
        var pattern = $"""\b(?:{pair.Key})\b""";

        if (!Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase))
        {
            continue;
        }

        var placeholder = $"{PlaceholderTokenPrefix}{index}{PlaceholderTokenSuffix}";
        text = Regex.Replace(text, pattern, placeholder, RegexOptions.IgnoreCase);
        placeholderValueMap[placeholder] = pair.Value;
        index++;
    }

    return (text, placeholderValueMap);
}

internal static string UnmaskTranslationPairs(string text, IReadOnlyDictionary<string, string> placeholderValueMap, ILogger logger)
{
    foreach (var (placeholder, value) in placeholderValueMap)
    {
        if (!text.Contains(placeholder))
        {
            logger.LogWarning(
                "Expected translation pair placeholder {Placeholder} was not found in the OpenAI response and could not be restored to {Value}.",
                placeholder,
                value);
            continue;
        }

        text = text.Replace(placeholder, value);
    }

    return text;
}
```

Add `using Microsoft.Extensions.Logging;` to the top of the file (the project already references `Microsoft.Extensions.Logging.Abstractions`).

Note: `TranslateTextAsync` and `TranslateHtmlAsync` still call `ReplaceTranslationPairs` at this point in the plan — leave those call sites broken for now; they are fixed in Task 3. The file will not compile between this step and Task 3's edits, which is expected for this intermediate step. (If your workflow requires the repo to build after every task, do Task 3's `OpenAiTranslationService.cs` edits together with this step rather than as a separate commit — see the note at the top of Task 3.)

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Aquifer.Jobs.UnitTests --filter "FullyQualifiedName~OpenAiTranslationServiceTests"`
Expected: the new masking/unmasking tests PASS. The project as a whole will not build yet because `TranslateTextAsync`/`TranslateHtmlAsync` still reference the deleted `ReplaceTranslationPairs` — that's expected until Task 3. If `dotnet test` refuses to run due to the build error, skip ahead and do Task 3's wiring changes in the same commit as this task (see Step 3 note above), then re-run.

- [ ] **Step 5: Commit**

```bash
git add src/Aquifer.AI/OpenAiTranslationService.cs tests/Aquifer.Jobs.UnitTests/Services/OpenAiTranslationServiceTests.cs
git commit -m "Replace translation pair value substitution with placeholder masking"
```

---

### Task 2: Add the placeholder-preservation instruction to the system prompts

**Files:**
- Modify: `src/Aquifer.AI/OpenAiTranslationService.cs`
- Test: `tests/Aquifer.Jobs.UnitTests/Services/OpenAiTranslationServiceTests.cs`

**Interfaces:**
- Consumes: `PlaceholderTokenPrefix`/`PlaceholderTokenSuffix` from Task 1 (for wording consistency only; not functionally required).
- Produces: `GetHtmlTranslationPrompt`, `GetHtmlTextImprovementPrompt`, `GetPlainTextTranslationPrompt` become `internal` (from `private`) so they can be exercised directly in tests, and each now appends `PlaceholderPreservationInstruction`.

- [ ] **Step 1: Write the failing tests**

Add to `tests/Aquifer.Jobs.UnitTests/Services/OpenAiTranslationServiceTests.cs`:

```csharp
private static OpenAiTranslationService CreateService(OpenAiTranslationOptions? translationOptions = null)
{
    return new OpenAiTranslationService(
        translationOptions ?? new OpenAiTranslationOptions
        {
            HtmlBasePrompt = "html-base-prompt",
            LanguageSpecificTextImprovementPromptAppendixByLanguageIso6393CodeMap = new Dictionary<string, string>(),
            PlainTextTranslationPromptFormatString = "translate-to-{0}",
            Temperature = Temperature,
            TextImprovementPromptFormatString = "improve-{0}",
            TranslationPromptFormatString = "translate-then-{0}",
        },
        CreateOptions(),
        new FakeAzureKeyVaultClient());
}

private sealed class FakeAzureKeyVaultClient : IAzureKeyVaultClient
{
    public Task<string> GetSecretAsync(string secretName) => Task.FromResult("fake-api-key");
}

[Fact]
public void GetHtmlTranslationPrompt_ShouldIncludeThePlaceholderPreservationInstruction()
{
    var service = CreateService();

    var prompt = service.GetHtmlTranslationPrompt(("ENG", "English"));

    Assert.Contains(OpenAiTranslationService.PlaceholderPreservationInstruction, prompt);
}

[Fact]
public void GetHtmlTextImprovementPrompt_ShouldIncludeThePlaceholderPreservationInstruction()
{
    var service = CreateService();

    var prompt = service.GetHtmlTextImprovementPrompt(("ENG", "English"));

    Assert.Contains(OpenAiTranslationService.PlaceholderPreservationInstruction, prompt);
}

[Fact]
public void GetPlainTextTranslationPrompt_ShouldIncludeThePlaceholderPreservationInstruction()
{
    var service = CreateService();

    var prompt = service.GetPlainTextTranslationPrompt(("ENG", "English"));

    Assert.Contains(OpenAiTranslationService.PlaceholderPreservationInstruction, prompt);
}
```

Add `using Aquifer.Common.Clients;` to the test file's usings for `IAzureKeyVaultClient`.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Aquifer.Jobs.UnitTests --filter "FullyQualifiedName~OpenAiTranslationServiceTests"`
Expected: FAIL to compile — `GetHtmlTranslationPrompt` etc. are `private` and `PlaceholderPreservationInstruction` doesn't exist yet.

- [ ] **Step 3: Add the instruction constant and wire it into the prompt builders**

In `src/Aquifer.AI/OpenAiTranslationService.cs`, add (near the other placeholder constants added in Task 1):

```csharp
internal const string PlaceholderPreservationInstruction =
    "The text may contain tokens formatted like ⟦AQP0⟧ (an opening ⟦ bracket, the letters " +
    "\"AQP\", one or more digits, and a closing ⟧ bracket). These tokens stand in for terminology a " +
    "human translator has already approved. Copy every such token into your output exactly as written, in " +
    "the same position, with no changes to its characters, and do not translate, explain, or remove it.";
```

Change the three prompt-builder methods from `private` to `internal` and append the instruction:

```csharp
internal string GetHtmlTranslationPrompt((string Iso6393Code, string EnglishName) destinationLanguage)
{
    var translationPrompt = string.Format(_options.TranslationPromptFormatString, destinationLanguage.EnglishName);

    return $"{_options.HtmlBasePrompt} {translationPrompt} {PlaceholderPreservationInstruction}";
}

internal string GetHtmlTextImprovementPrompt((string Iso6393Code, string EnglishName) destinationLanguage)
{
    var textImprovementPrompt = string.Format(_options.TextImprovementPromptFormatString, destinationLanguage.EnglishName);

    var languageSpecificTextImprovementPromptAppendix =
        _options.LanguageSpecificTextImprovementPromptAppendixByLanguageIso6393CodeMap
            .GetValueOrDefault(destinationLanguage.Iso6393Code.ToUpper());

    return
        $"{_options.HtmlBasePrompt} {textImprovementPrompt}{(languageSpecificTextImprovementPromptAppendix == null ? "" : $" {languageSpecificTextImprovementPromptAppendix}")} {PlaceholderPreservationInstruction}";
}

internal string GetPlainTextTranslationPrompt((string Iso6393Code, string EnglishName) destinationLanguage)
{
    return $"{string.Format(_options.PlainTextTranslationPromptFormatString, destinationLanguage.EnglishName)} {PlaceholderPreservationInstruction}";
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Aquifer.Jobs.UnitTests --filter "FullyQualifiedName~OpenAiTranslationServiceTests"`
Expected: PASS. (Full-project build may still fail until Task 3 fixes the `TranslateTextAsync`/`TranslateHtmlAsync` call sites — see the note in Task 1 Step 3. If so, verify these specific facts compile and pass by temporarily doing this task's edits together with Task 3's, exactly as with Task 1.)

- [ ] **Step 5: Commit**

```bash
git add src/Aquifer.AI/OpenAiTranslationService.cs tests/Aquifer.Jobs.UnitTests/Services/OpenAiTranslationServiceTests.cs
git commit -m "Instruct OpenAI to preserve translation pair placeholder tokens"
```

---

### Task 3: Wire masking into `TranslateTextAsync`/`TranslateHtmlAsync` and add logging

**Files:**
- Modify: `src/Aquifer.AI/OpenAiTranslationService.cs`

**Interfaces:**
- Consumes: `TryGetFullTranslationPairReplacement`, `MaskTranslationPairs`, `UnmaskTranslationPairs` (Task 1); `GetHtmlTranslationPrompt`/`GetHtmlTextImprovementPrompt`/`GetPlainTextTranslationPrompt` (Task 2, unchanged signatures).
- Produces: constructor signature becomes `OpenAiTranslationService(OpenAiTranslationOptions, OpenAiOptions, IAzureKeyVaultClient, ILogger<OpenAiTranslationService>)` — the DI container resolves `ILogger<T>` automatically, no registration change needed in `Aquifer.Jobs/Program.cs`.

This task makes the project buildable again after Tasks 1 and 2's intermediate breakage. There's no new isolated unit test here — the existing tests from Tasks 1 and 2 are the coverage; this task is verified by a full build plus the existing test suite.

- [ ] **Step 1: Add the `ILogger` field and constructor parameter**

In `src/Aquifer.AI/OpenAiTranslationService.cs`, update the field list and constructor:

```csharp
private readonly ChatClient _chatClient;
private readonly ILogger<OpenAiTranslationService> _logger;

private readonly OpenAiOptions _openAiOptions;
private readonly OpenAiTranslationOptions _options;
private readonly float _temperature;

public OpenAiTranslationService(
    OpenAiTranslationOptions openAiTranslationOptions,
    OpenAiOptions openAiOptions,
    IAzureKeyVaultClient keyVaultClient,
    ILogger<OpenAiTranslationService> logger)
{
    _openAiOptions = openAiOptions;
    _options = openAiTranslationOptions;
    _logger = logger;

    const string openAiApiKeySecretName = "OpenAiApiKey";

    // TODO Inject a ChatClient instead of building one here. This will require changing how we fetch key vault secrets.
#pragma warning disable VSTHRD002
    var openApiKey = keyVaultClient.GetSecretAsync(openAiApiKeySecretName).GetAwaiter().GetResult();
#pragma warning restore VSTHRD002

    _chatClient = new ChatClient(
        openAiOptions.Model,
        new ApiKeyCredential(openApiKey),
        new OpenAIClientOptions
        {
            NetworkTimeout = s_openAiNetworkTimeout,
        });

    _temperature = _options.Temperature;
}
```

- [ ] **Step 2: Rewire `TranslateTextAsync` to mask/unmask**

Replace:

```csharp
        var prompt = GetPlainTextTranslationPrompt(destinationLanguage);

        var (textWithReplacements, isFullReplacement) = ReplaceTranslationPairs(text, translationPairs);

        return isFullReplacement
            ? textWithReplacements
            : await CompleteChatAsync(prompt, textWithReplacements, cancellationToken);
```

with:

```csharp
        var fullReplacement = TryGetFullTranslationPairReplacement(text, translationPairs);

        if (fullReplacement != null)
        {
            return fullReplacement;
        }

        var prompt = GetPlainTextTranslationPrompt(destinationLanguage);
        var (maskedText, placeholderValueMap) = MaskTranslationPairs(text, translationPairs);

        var translatedText = await CompleteChatAsync(prompt, maskedText, cancellationToken);

        return UnmaskTranslationPairs(translatedText, placeholderValueMap, _logger);
```

- [ ] **Step 3: Rewire `TranslateHtmlAsync`'s paragraph pipeline to mask/unmask**

Replace:

```csharp
            var paragraphTranslationTasks = paragraphs
                .Select(paragraph => HtmlUtilities.ProcessHtmlContentAsync(
                    paragraph,
                    async minifiedHtmlChunk =>
                    {
                        var (minifiedHtmlChunkWithReplacements, _) = ReplaceTranslationPairs(minifiedHtmlChunk, translationPairs);

                        var translatedHtmlChunk = htmlTranslationPrompt == null
                            ? minifiedHtmlChunkWithReplacements
                            : await CompleteChatAsync(htmlTranslationPrompt, minifiedHtmlChunkWithReplacements, cancellationToken);

                        return await CompleteChatAsync(htmlTextImprovementPrompt, translatedHtmlChunk, cancellationToken);
                    }))
                .ToList();
```

with:

```csharp
            var paragraphTranslationTasks = paragraphs
                .Select(paragraph => HtmlUtilities.ProcessHtmlContentAsync(
                    paragraph,
                    async minifiedHtmlChunk =>
                    {
                        var (maskedHtmlChunk, placeholderValueMap) = MaskTranslationPairs(minifiedHtmlChunk, translationPairs);

                        var translatedHtmlChunk = htmlTranslationPrompt == null
                            ? maskedHtmlChunk
                            : await CompleteChatAsync(htmlTranslationPrompt, maskedHtmlChunk, cancellationToken);

                        var improvedHtmlChunk = await CompleteChatAsync(htmlTextImprovementPrompt, translatedHtmlChunk, cancellationToken);

                        return UnmaskTranslationPairs(improvedHtmlChunk, placeholderValueMap, _logger);
                    }))
                .ToList();
```

- [ ] **Step 4: Build and run the full test suite**

Run: `dotnet build src/Aquifer.AI/Aquifer.AI.csproj`
Expected: builds with no errors (no more references to the deleted `ReplaceTranslationPairs`).

Run: `dotnet test tests/Aquifer.Jobs.UnitTests --filter "FullyQualifiedName~OpenAiTranslationServiceTests"`
Expected: all tests (from Tasks 1, 2, and the pre-existing `CreateChatCompletionOptions` tests) PASS.

Run: `dotnet build Aquifer.sln`
Expected: the whole solution builds — this confirms `Aquifer.Jobs`'s DI registration (`services.AddSingleton<ITranslationService, OpenAiTranslationService>();` in `src/Aquifer.Jobs/Program.cs`) still resolves, since `ILogger<OpenAiTranslationService>` is satisfied automatically by the already-registered `ILoggerFactory`/Application Insights logging setup — no change needed there.

- [ ] **Step 5: Commit**

```bash
git add src/Aquifer.AI/OpenAiTranslationService.cs
git commit -m "Mask translation pairs before OpenAI calls and restore them after"
```

---

### Task 4: Update the workflow doc to describe the new mechanism

**Files:**
- Modify: `docs/ai-translation-workflow.md`

- [ ] **Step 1: Update the "user message content" row and add a placeholder-masking note**

In the "Where the OpenAI request parameters come from" table, change the "User message content" row's description from mentioning direct substitution to mentioning masking, and add a short paragraph after the table (or as a new subsection) explaining:

```markdown
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
```

- [ ] **Step 2: Commit**

```bash
git add docs/ai-translation-workflow.md
git commit -m "Document translation pair placeholder masking in the workflow doc"
```

---

## Self-Review Notes

- **Spec coverage:** The spec (`design.md`) asks that every translation pair present going into a chunk survive both passes unchanged, model-agnostically — Task 1 (masking core) + Task 3 (wiring) deliver this directly; Task 2 (explicit prompt instruction) covers the spec's implicit ask that the fix not merely rely on model passivity; Task 4 keeps documentation in sync.
- **Placeholder scan:** no TBD/TODO placeholders were introduced; the one pre-existing `// TODO` comment in the constructor is untouched, pre-existing tech debt unrelated to this fix.
- **Type consistency:** `MaskTranslationPairs` returns `(string MaskedText, IReadOnlyDictionary<string, string> PlaceholderValueMap)` consistently across Task 1's helper and Task 3's two call sites; `UnmaskTranslationPairs(string, IReadOnlyDictionary<string, string>, ILogger)` signature matches at both call sites in Task 3.
