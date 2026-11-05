using System.Text.Json;

namespace KnowledgeAgent.Features.Chat.Tools;

internal static class ToolSchemas
{
    public static readonly string[] SourceStatuses = ["pending", "processing", "ready", "failed"];

    public static readonly string[] SourceTypes = ["url", "pdf", "text", "markdown"];

    public static JsonElement SearchKnowledgeBase(int defaultTopK, int maxTopK) => Parse($$"""
        {
          "type": "object",
          "properties": {
            "query": {
              "type": "string",
              "description": "Natural-language search query. Be specific; rephrase and search again when results are weak."
            },
            "top_k": {
              "type": "integer",
              "minimum": 1,
              "maximum": {{maxTopK}},
              "description": "Number of passages to return (default {{defaultTopK}}, max {{maxTopK}})."
            },
            "source_ids": {
              "type": "array",
              "items": { "type": "string" },
              "description": "Optional source ids (from find_sources or earlier results) to restrict the search to."
            }
          },
          "required": ["query"],
          "additionalProperties": false
        }
        """);

    public static JsonElement GrepSources(int defaultPassages, int maxPassages, int defaultSources, int maxSources, int maxTextLength) => Parse($$"""
        {
          "type": "object",
          "properties": {
            "text": {
              "type": "string",
              "maxLength": {{maxTextLength}},
              "description": "The exact word, name, number, code or phrase to find. Not a regular expression: every character must match as written, accents included, except that whitespace matches any whitespace."
            },
            "match_case": {
              "type": "boolean",
              "description": "Match upper and lower case exactly (default false), e.g. to tell the acronym US from the word us."
            },
            "whole_words": {
              "type": "boolean",
              "description": "Match whole words only (default true), so art does not match article. Set false to match inside longer words, such as a word stem or part of a code."
            },
            "output": {
              "type": "string",
              "enum": ["passages", "sources"],
              "description": "passages (default): the matching passages with citation tags. sources: each source with its number of matching passages, most first, to list or count the sources that mention the text."
            },
            "source_ids": {
              "type": "array",
              "items": { "type": "string" },
              "description": "Optional source ids (from find_sources or earlier results) to restrict the search to."
            },
            "limit": {
              "type": "integer",
              "minimum": 1,
              "maximum": {{maxSources}},
              "description": "Results per page: passages default {{defaultPassages}}, max {{maxPassages}}; sources default {{defaultSources}}, max {{maxSources}}."
            },
            "offset": {
              "type": "integer",
              "minimum": 0,
              "description": "Number of results to skip; pass the offset from a previous result's paging hint to get the next page."
            }
          },
          "required": ["text"],
          "additionalProperties": false
        }
        """);

    public static JsonElement FindSources(int defaultLimit, int maxLimit) => Parse($$"""
        {
          "type": "object",
          "properties": {
            "query": {
              "type": "string",
              "description": "Words to look for in the source title, URL or file name (case-insensitive; every word must match). Omit to list sources newest first."
            },
            "type": {
              "type": "string",
              "enum": ["url", "pdf", "text", "markdown"],
              "description": "Only sources of this type."
            },
            "status": {
              "type": "string",
              "enum": ["ready", "pending", "processing", "failed", "any"],
              "description": "Processing status to return (default ready, the searchable sources)."
            },
            "limit": {
              "type": "integer",
              "minimum": 1,
              "maximum": {{maxLimit}},
              "description": "Results per page (default {{defaultLimit}}, max {{maxLimit}})."
            },
            "offset": {
              "type": "integer",
              "minimum": 0,
              "description": "Number of results to skip; pass the offset from a previous result's paging hint to get the next page."
            }
          },
          "additionalProperties": false
        }
        """);

    public static JsonElement ReadSource(int defaultMaxChars, int maxChars) => Parse($$"""
        {
          "type": "object",
          "properties": {
            "source_id": {
              "type": "string",
              "description": "Id of a ready source (from find_sources or the source_id of a search result)."
            },
            "offset": {
              "type": "integer",
              "minimum": 0,
              "description": "Character offset to start reading at (default 0). Pass the 'next offset' of a previous read to continue."
            },
            "max_chars": {
              "type": "integer",
              "minimum": 1,
              "maximum": {{maxChars}},
              "description": "Maximum number of characters to return (default {{defaultMaxChars}}, max {{maxChars}})."
            }
          },
          "required": ["source_id"],
          "additionalProperties": false
        }
        """);

    public static JsonElement DelegateTask() => Parse("""
        {
          "type": "object",
          "properties": {
            "task": {
              "type": "string",
              "description": "Self-contained description of what to research and what the report must cover."
            },
            "source_ids": {
              "type": "array",
              "items": { "type": "string" },
              "description": "Optional ids of sources the sub-agent should focus on."
            }
          },
          "required": ["task"],
          "additionalProperties": false
        }
        """);

    public static JsonElement RecordFeedback() => Parse("""
        {
          "type": "object",
          "properties": {
            "sentiment": {
              "type": "string",
              "enum": ["positive", "neutral", "negative"],
              "description": "How the person feels about what they are giving feedback on."
            },
            "comment": {
              "type": "string",
              "description": "Their feedback, faithfully summarized in their own words, at most 2000 characters."
            }
          },
          "required": ["sentiment", "comment"],
          "additionalProperties": false
        }
        """);

    public static JsonElement RequestHumanHandover() => Parse("""
        {
          "type": "object",
          "properties": {
            "reason": {
              "type": "string",
              "description": "Why a person is needed, e.g. 'asked for a person' or 'question not covered by the knowledge base'."
            },
            "summary": {
              "type": "string",
              "description": "Two or three sentences for the support team: who the visitor is if known, what they need and what you already told them."
            }
          },
          "required": ["reason", "summary"],
          "additionalProperties": false
        }
        """);

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
