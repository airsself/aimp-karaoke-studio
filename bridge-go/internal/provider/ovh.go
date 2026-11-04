package provider

import(
	"context"
	"encoding/json"
	"net/url"
	"strings"
	"github.com/airsself/aimp-karaoke-studio/bridge-go/internal/lrc"
	"github.com/airsself/aimp-karaoke-studio/bridge-go/internal/model"
)

type LyricsOVH struct{Client *Client; BaseURL string}
func(p LyricsOVH) Name()string{return "lyrics.ovh"}
func(p LyricsOVH) Search(ctx context.Context,t model.Track)([]model.Candidate,error){
	u:=strings.TrimRight(p.BaseURL,"/")+"/v1/"+url.PathEscape(t.Artist)+"/"+url.PathEscape(t.Title)
	data,err:=p.Client.Get(ctx,u)
	if err!=nil||len(data)==0{return nil,err}
	var body struct{Lyrics string \`json:"lyrics"\`}
	if err=json.Unmarshal(data,&body);err!=nil{return nil,err}
	lyrics:=strings.TrimSpace(body.Lyrics)
	if len(lyrics)<4||len(lyrics)>262144{return nil,nil}
	return []model.Candidate{{Track:t,Source:p.Name(),Content:lyrics,Synced:lrc.Synced(lyrics)}},nil
}
