package provider

import (
	"context"
	"encoding/json"
	"net/url"
	"strconv"
	"strings"
	"github.com/airsself/aimp-karaoke-studio/bridge-go/internal/lrc"
	"github.com/airsself/aimp-karaoke-studio/bridge-go/internal/model"
)

type LRCLIB struct{Client *Client; BaseURL string}
func(p LRCLIB)Name()string{return "lrclib"}
type lrcRow struct {
	Artist string \`json:"artistName"\`
	Title string \`json:"trackName"\`
	Album string \`json:"albumName"\`
	Duration float64 \`json:"duration"\`
	Synced string \`json:"syncedLyrics"\`
	Plain string \`json:"plainLyrics"\`
	Instrumental bool \`json:"instrumental"\`
}
func(p LRCLIB)Search(ctx context.Context,t model.Track)([]model.Candidate,error){
	base:=strings.TrimRight(p.BaseURL,"/")
	exact:=url.Values{}
	exact.Set("artist_name",t.Artist)
	exact.Set("track_name",t.Title)
	if t.Album!="" {exact.Set("album_name",t.Album)}
	if t.DurationSeconds>0{exact.Set("duration",strconv.FormatFloat(t.DurationSeconds,'f',0,64))}
	result:=make([]model.Candidate,0,20)
	data,err:=p.Client.Get(ctx,base+"/api/get?"+exact.Encode())
	if err!=nil{return result,err}
	if len(data)>0 {
		var row lrcRow
		if json.Unmarshal(data,&row)==nil {if c,ok:=candidate(row);ok{result=append(result,c)}}
	}
	search:=url.Values{}
	search.Set("artist_name",t.Artist);search.Set("track_name",t.Title)
	data,err=p.Client.Get(ctx,base+"/api/search?"+search.Encode())
	if err!=nil {if len(result)>0{return result,nil};return nil,err}
	if len(data)>0{
		var rows []lrcRow
		if err:=json.Unmarshal(data,&rows);err!=nil{return result,err}
		for i,row:=range rows{
			if i>=20{break}
			if c,ok:=candidate(row);ok{result=append(result,c)}
		}
	}
	return result,nil
}
func candidate(r lrcRow)(model.Candidate,bool){
	text:=strings.TrimSpace(r.Synced)
	if text=="" {text=strings.TrimSpace(r.Plain)}
	if r.Instrumental || len(text)<4 || len(text)>262144||r.Artist==""||r.Title=="" {
		return model.Candidate{},false
	}
	return model.Candidate{
		Track:model.Track{Artist:r.Artist,Title:r.Title,Album:r.Album,DurationSeconds:r.Duration},
		Source:"lrclib",Content:text,Synced:lrc.Synced(text)},true
}
