-- Migration: AddGamePersonalization
-- Date: 2026-01-25
-- Description: Adds metadata_json to game_sessions and creates game_outcomes table for outcome tracking

-- ===========================================
-- Part 1: Add metadata_json to game_sessions
-- ===========================================

ALTER TABLE game_sessions
ADD COLUMN IF NOT EXISTS metadata_json JSONB NULL;

COMMENT ON COLUMN game_sessions.metadata_json IS 'Game session metadata including difficulty, tone, bucket, intent alignment';

-- ===========================================
-- Part 2: Create game_outcomes table
-- ===========================================

CREATE TABLE IF NOT EXISTS game_outcomes (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    session_id UUID NOT NULL,
    game_type VARCHAR(50) NOT NULL,
    initiator_user_id INTEGER NOT NULL,
    partner_user_id INTEGER NOT NULL,
    match_id UUID NOT NULL,

    -- Game configuration
    difficulty VARCHAR(20) NOT NULL DEFAULT 'MEDIUM',
    tone VARCHAR(20) NOT NULL DEFAULT 'BALANCED',
    bucket VARCHAR(30) NOT NULL DEFAULT 'EXPLORER',
    intent_alignment DOUBLE PRECISION NOT NULL DEFAULT 0.5,

    -- Game results
    total_rounds INTEGER NOT NULL,
    completed_rounds INTEGER NOT NULL,
    initiator_score INTEGER NOT NULL DEFAULT 0,
    partner_score INTEGER NOT NULL DEFAULT 0,
    average_response_time_ms DOUBLE PRECISION NOT NULL DEFAULT 0,

    -- Completion status
    completion_status VARCHAR(30) NOT NULL DEFAULT 'COMPLETED',
    user_feedback TEXT NULL,

    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    -- Foreign keys
    CONSTRAINT fk_game_outcomes_session FOREIGN KEY (session_id)
        REFERENCES game_sessions(id) ON DELETE RESTRICT,
    CONSTRAINT fk_game_outcomes_initiator FOREIGN KEY (initiator_user_id)
        REFERENCES users(id) ON DELETE RESTRICT,
    CONSTRAINT fk_game_outcomes_partner FOREIGN KEY (partner_user_id)
        REFERENCES users(id) ON DELETE RESTRICT,
    CONSTRAINT fk_game_outcomes_match FOREIGN KEY (match_id)
        REFERENCES matches(id) ON DELETE RESTRICT
);

-- ===========================================
-- Part 3: Create indexes on game_outcomes
-- ===========================================

-- Unique constraint on session_id (one outcome per session)
CREATE UNIQUE INDEX IF NOT EXISTS idx_game_outcomes_session_id
    ON game_outcomes(session_id);

-- Index for user-based queries
CREATE INDEX IF NOT EXISTS idx_game_outcomes_initiator_user_id
    ON game_outcomes(initiator_user_id);

CREATE INDEX IF NOT EXISTS idx_game_outcomes_partner_user_id
    ON game_outcomes(partner_user_id);

-- Index for match-based queries
CREATE INDEX IF NOT EXISTS idx_game_outcomes_match_id
    ON game_outcomes(match_id);

-- Composite indexes for time-series queries
CREATE INDEX IF NOT EXISTS idx_game_outcomes_initiator_created
    ON game_outcomes(initiator_user_id, created_at DESC);

CREATE INDEX IF NOT EXISTS idx_game_outcomes_partner_created
    ON game_outcomes(partner_user_id, created_at DESC);

-- Index for analytics queries
CREATE INDEX IF NOT EXISTS idx_game_outcomes_game_type_status
    ON game_outcomes(game_type, completion_status);

CREATE INDEX IF NOT EXISTS idx_game_outcomes_difficulty_tone
    ON game_outcomes(difficulty, tone);

-- ===========================================
-- Part 4: Add comments for documentation
-- ===========================================

COMMENT ON TABLE game_outcomes IS 'Tracks outcomes of game sessions for analytics and feedback loops';
COMMENT ON COLUMN game_outcomes.difficulty IS 'Game difficulty: EASY, MEDIUM, HARD';
COMMENT ON COLUMN game_outcomes.tone IS 'Game tone: PLAYFUL, BALANCED, THOUGHTFUL';
COMMENT ON COLUMN game_outcomes.bucket IS 'Match bucket: CORE_FIT, LIFESTYLE_FIT, CONVERSATION_FIT, EXPLORER';
COMMENT ON COLUMN game_outcomes.completion_status IS 'Status: COMPLETED, ABANDONED, EXPIRED';

-- ===========================================
-- ROLLBACK SCRIPT (save separately if needed)
-- ===========================================
-- DROP TABLE IF EXISTS game_outcomes;
-- ALTER TABLE game_sessions DROP COLUMN IF EXISTS metadata_json;
