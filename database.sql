-- Disable RLS for all tables implicitly by not adding it

-- Create profiles table
CREATE TABLE IF NOT EXISTS public.profiles (
    id TEXT PRIMARY KEY,
    email TEXT,
    username TEXT,
    title TEXT,
    avatar_url TEXT,
    provider_type TEXT,
    rank TEXT,
    total_playtime INTEGER DEFAULT 0,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()),
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()),
    rawg_api_key TEXT
);

-- Create user_games table
CREATE TABLE IF NOT EXISTS public.user_games (
    id TEXT PRIMARY KEY,
    user_id TEXT,
    game_id TEXT,
    game_title TEXT,
    playtime_minutes INTEGER DEFAULT 0,
    last_played TIMESTAMP WITH TIME ZONE,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()),
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now())
);

-- Insert bucket for avatars
INSERT INTO storage.buckets (id, name, public) 
VALUES ('avatars', 'avatars', true)
ON CONFLICT (id) DO NOTHING;
