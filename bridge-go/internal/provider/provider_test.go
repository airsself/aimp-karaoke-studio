package provider

import (
	"context"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
	"time"
	"github.com/airsself/aimp-karaoke-studio/bridge-go/internal/model"
)
func TestLRCLIBAndOVH(t *testing.T){
	server:=httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter,r *http.Request){
		switch {
		case strings.HasPrefix(r.URL.Path,"/api/get"):
			w.Write([]byte(`{"artistName":"Demo","trackName":"Night","duration":200,"syncedLyrics":"[00:01.00]hello","plainLyrics":"hello"}`))
		case strings.HasPrefix(r.URL.Path,"/api/search"):
			w.Write([]byte(`[{"artistName":"Demo","trackName":"Night","duration":200,"plainLyrics":"hello world"},{"instrumental":true}]`))
		case strings.HasPrefix(r.URL.Path,"/v1/"):
			w.Write([]byte(`{"lyrics":"hello plain text"}`))
		default:http.NotFound(w,r)
		}
	}))
	defer server.Close()
	client:=NewClient(time.Second)
	song:=model.Track{Artist:"Demo",Title:"Night",DurationSeconds:200}
	rows,err:=(LRCLIB{Client:client,BaseURL:server.URL}).Search(context.Background(),song)
	if err!=nil||len(rows)!=2||!rows[0].Synced||rows[1].Synced{t.Fatalf("lrclib=%v %v",rows,err)}
	plain,err:=(LyricsOVH{Client:client,BaseURL:server.URL}).Search(context.Background(),song)
	if err!=nil||len(plain)!=1||plain[0].Synced{t.Fatalf("ovh=%v %v",plain,err)}
}
func TestHTTPSizeLimit(t *testing.T){
	server:=httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter,r *http.Request){
		w.Write([]byte(strings.Repeat("X",maxBody+1)))
	}))
	defer server.Close()
	_,err:=NewClient(time.Second).Get(context.Background(),server.URL)
	if err==nil{t.Fatal("oversized provider body accepted")}
}
