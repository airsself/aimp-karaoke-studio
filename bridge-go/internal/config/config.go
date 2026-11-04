// Package config validates a subset of the documented v2.5 reference JSON.
// Unknown options (including the original proprietary management API options)
// are retained in the sample config but NOT silently claimed as implemented.
package config

import (
	"encoding/json"
	"fmt"
	"os"
	"strings"
	"time"
)

type Config struct {
	ConfigVersion int `json:"config_version"`
	Listen string `json:"listen"`
	ClientIdentity string `json:"client_identity"`
	ProviderOrder []string `json:"provider_order"`
	LRCLIBEnabled bool `json:"lrclib_enabled"`
	LRCLIBBaseURL string `json:"lrclib_base_url"`
	LyricsOVHEnabled bool `json:"lyrics_ovh_enabled"`
	LyricsOVHBaseURL string `json:"lyrics_ovh_base_url"`
	RequestTimeoutSeconds int `json:"request_timeout_seconds"`
	RetryCount int `json:"retry_count"`
	RetryBackoffMS int `json:"retry_backoff_ms"`
	MinRequestIntervalMS int `json:"min_request_interval_ms"`
	MinMatchScore float64 `json:"min_match_score"`
	EarlyAcceptScore float64 `json:"early_accept_score"`
	PreferSynced bool `json:"prefer_synced"`
	FallbackToPlain bool `json:"fallback_to_plain"`
	CacheTTLHours int `json:"cache_ttl_hours"`
	NegativeCacheTTLHours int `json:"negative_cache_ttl_hours"`
	CacheMaxEntries int `json:"cache_max_entries"`
	CacheDir string `json:"cache_dir"`
	MaxLyricsBytes int `json:"max_lyrics_bytes"`
}

func Default() Config {
	return Config{
		ConfigVersion: 4, Listen: "127.0.0.1:17321",
		ClientIdentity: "AimpKaraokeStudio-GoBridge/4.1",
		ProviderOrder: []string{"lrclib", "lyrics.ovh"},
		LRCLIBEnabled: true, LRCLIBBaseURL: "https://lrclib.net",
		LyricsOVHEnabled: true, LyricsOVHBaseURL: "https://api.lyrics.ovh",
		RequestTimeoutSeconds: 12, RetryCount: 2, RetryBackoffMS: 350,
		MinRequestIntervalMS: 300,
		MinMatchScore: 0.78, EarlyAcceptScore: 0.94,
		PreferSynced: true, FallbackToPlain: true,
		CacheTTLHours: 720, NegativeCacheTTLHours: 6,
		CacheMaxEntries: 3000, CacheDir: "cache", MaxLyricsBytes: 262144,
	}
}

func Load(path string) (Config, error) {
	cfg := Default()
	if path != "" {
		data, err := os.ReadFile(path)
		if err != nil { return cfg, fmt.Errorf("read config: %w", err) }
		if len(data)>128*1024 {return cfg,fmt.Errorf("config too large")}
		if err=json.Unmarshal(data,&cfg);err!=nil{return cfg,fmt.Errorf("parse config: %w",err)}
	}
	return cfg, cfg.Validate()
}
func (c Config) Validate() error {
	if !strings.HasPrefix(c.Listen,"127.0.0.1:") && !strings.HasPrefix(c.Listen,"[::1]:") {
		return fmt.Errorf("bridge must listen only on loopback")
	}
	if c.RequestTimeoutSeconds<1 || c.RequestTimeoutSeconds>60 ||
		c.RetryCount<0 || c.RetryCount>5 || c.RetryBackoffMS<0 || c.RetryBackoffMS>10000 ||
		c.MinRequestIntervalMS<0 || c.MinRequestIntervalMS>30000 {
		return fmt.Errorf("invalid network budgets")
	}
	if c.MinMatchScore<0 || c.MinMatchScore>1 || c.EarlyAcceptScore<c.MinMatchScore ||
		c.EarlyAcceptScore>1 {return fmt.Errorf("invalid score thresholds")}
	if c.CacheMaxEntries<1 || c.CacheMaxEntries>50000 ||
		c.CacheTTLHours<1 || c.CacheTTLHours>8760 ||
		c.NegativeCacheTTLHours<1 || c.NegativeCacheTTLHours>168 {
		return fmt.Errorf("invalid cache policy")
	}
	if c.MaxLyricsBytes<1024 || c.MaxLyricsBytes>1<<20 {
		return fmt.Errorf("max_lyrics_bytes outside safe range")
	}
	if !c.LRCLIBEnabled&&!c.LyricsOVHEnabled{return fmt.Errorf("no providers enabled")}
	for _,name:=range c.ProviderOrder {
		switch name {case "lrclib","lyrics.ovh","bandcamp","songlyrics":
		default:return fmt.Errorf("unknown provider: %q",name)}
	}
	return nil
}
func (c Config) Timeout() time.Duration{return time.Duration(c.RequestTimeoutSeconds)*time.Second}
