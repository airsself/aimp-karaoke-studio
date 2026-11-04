package model

import "strings"

// Track identifies a playback file without including its path or private tags.
type Track struct {
	Artist string `json:"artist"`
	Title string `json:"title"`
	Album string `json:"album,omitempty"`
	DurationSeconds float64 `json:"duration_seconds,omitempty"`
}

func (t Track) Valid() bool {
	return strings.TrimSpace(t.Artist) != "" && strings.TrimSpace(t.Title) != "" &&
		len(t.Artist) <= 2048 && len(t.Title) <= 2048 && len(t.Album) <= 2048
}

type Candidate struct {
	Track Track `json:"track"`
	Source string `json:"source"`
	Content string `json:"content"`
	Synced bool `json:"synced"`
	Score float64 `json:"score"`
}

type Result struct {
	Best *Candidate `json:"best,omitempty"`
	Candidates []Candidate `json:"candidates"`
	Cached bool `json:"cached"`
	Diagnostic string `json:"diagnostic,omitempty"`
}
