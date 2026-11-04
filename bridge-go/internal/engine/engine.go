// Package engine orchestrates independently testable providers, score ranking
// and caching. It deliberately has no dependency on HTTP request handlers.
package engine

import (
	"context"
	"errors"
	"fmt"
	"sort"
	"strings"
	"time"
	"github.com/airsself/aimp-karaoke-studio/bridge-go/internal/cache"
	"github.com/airsself/aimp-karaoke-studio/bridge-go/internal/config"
	"github.com/airsself/aimp-karaoke-studio/bridge-go/internal/model"
	"github.com/airsself/aimp-karaoke-studio/bridge-go/internal/provider"
	"github.com/airsself/aimp-karaoke-studio/bridge-go/internal/scoring"
)
type Engine struct{
	Config config.Config
	Providers []provider.Provider
	Cache *cache.Store
}
func New(cfg config.Config,c *cache.Store,p ...provider.Provider)*Engine{
	return &Engine{Config:cfg,Cache:c,Providers:p}
}
func(e *Engine)Search(ctx context.Context,track model.Track,force bool)(model.Result,error) {
	if !track.Valid(){return model.Result{},errors.New("missing or oversized artist/title")}
	if !force&&e.Cache!=nil {
		if entry,found:=e.Cache.Get(track);found {
			r:=model.Result{Best:entry.Best,Cached:true,Diagnostic:"cache"}
			if entry.Best!=nil{r.Candidates=[]model.Candidate{*entry.Best}}
			return r,nil
		}
	}
	var all []model.Candidate
	var providerErrors []string
	seen:=make(map[string]bool)
	for _,p:=range e.Providers{
		if err:=ctx.Err();err!=nil{return model.Result{},err}
		rows,err:=p.Search(ctx,track)
		if err!=nil {
			if ctx.Err()!=nil{return model.Result{},ctx.Err()}
			providerErrors=append(providerErrors,fmt.Sprintf("%s: %v",p.Name(),err))
			continue
		}
		for _,row:=range rows {
			if len(row.Content)<4||len(row.Content)>e.Config.MaxLyricsBytes{continue}
			row.Score=scoring.Score(track,row.Track)
			if row.Score<e.Config.MinMatchScore{continue}
			key:=strings.ToLower(row.Source)+"|"+row.Content
			if seen[key]{continue};seen[key]=true
			all=append(all,row)
		}
		if e.Config.PreferSynced && hasEarlySynced(all,e.Config.EarlyAcceptScore){break}
	}
	sort.SliceStable(all,func(i,j int)bool{
		a,b:=all[i],all[j]
		af,bf:=a.Score,b.Score
		if e.Config.PreferSynced {
			if a.Synced {af+=.04};if b.Synced {bf+=.04}
		}
		if af==bf{return a.Score>b.Score}
		return af>bf
	})
	if len(all)>24{all=all[:24]}
	var best *model.Candidate
	if len(all)>0 {
		if all[0].Synced||e.Config.FallbackToPlain {
			c:=all[0];best=&c
		}
	}
	result:=model.Result{Best:best,Candidates:all,Diagnostic:"ok"}
	if len(all)==0 {
		if len(providerErrors)>0{result.Diagnostic=strings.Join(providerErrors,"; ")} else {result.Diagnostic="not_found"}
	}
	// A failed provider should not poison the negative cache: retry later.
	if e.Cache!=nil && (best!=nil||len(providerErrors)==0){
		ttl:=time.Duration(e.Config.CacheTTLHours)*time.Hour
		if best==nil{ttl=time.Duration(e.Config.NegativeCacheTTLHours)*time.Hour}
		if err:=e.Cache.Put(track,best,ttl);err!=nil{
			// An unavailable disk cache must not interfere with playback.
			result.Diagnostic+=" (cache unavailable)"
		}
	}
	return result,nil
}
func hasEarlySynced(candidates []model.Candidate,threshold float64)bool{
	for _,c:=range candidates{if c.Synced&&c.Score>=threshold{return true}}
	return false
}
