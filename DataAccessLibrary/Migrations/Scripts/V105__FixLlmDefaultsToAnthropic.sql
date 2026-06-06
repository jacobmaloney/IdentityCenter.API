-- V105: Fix LLM defaults to Anthropic for unmodified-default installs
-- V005 seeded Provider=OpenAI / Endpoint=api.openai.com / Model=gpt-3.5-turbo as
-- the initial defaults. V104 only fixed the model field for rows already on stale
-- Claude variants, so fresh installs still inherit the OpenAI defaults and fail
-- the first chat call against the Anthropic-shaped LlmService.
--
-- This migration only touches rows that ALL THREE columns still match the V005
-- defaults verbatim — if a user has set even one field, we leave the row alone.

IF EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID('SystemConfigurations') AND type = 'U')
BEGIN
    UPDATE SystemConfigurations
    SET ChatLlmProvider = 'Anthropic',
        ChatLlmEndpoint = 'https://api.anthropic.com/v1',
        ChatLlmModel    = 'claude-sonnet-4-6'
    WHERE ChatLlmProvider = 'OpenAI'
      AND ChatLlmEndpoint = 'https://api.openai.com/v1'
      AND ChatLlmModel    = 'gpt-3.5-turbo';
END
