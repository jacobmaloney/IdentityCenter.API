-- V104: Fix LLM model name in the CORRECT table (SystemConfigurations, not SystemConfiguration)
-- V103 updated the wrong table. The actual config is stored as typed columns, not key-value.

IF EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID('SystemConfigurations') AND type = 'U')
BEGIN
    UPDATE SystemConfigurations SET ChatLlmModel = 'claude-sonnet-4-6'
    WHERE ChatLlmModel LIKE '%claude-sonnet-4-5%'
       OR ChatLlmModel LIKE '%claude-3-haiku%'
       OR ChatLlmModel LIKE '%20241022%';
END
