package httpapi

import(
	"context"
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"net/url"
	"strings"
	"testing"
	"github.com/airsself/aimp-karaoke-studio/bridge-go/internal/model"
)
type finder struct{}
func(finder)Search(_ context.Context,t model.Track,_ bool)(model.Result,error){
	c:=model.Candidate{Track:t,Source:"fake",Content:"[00:01.00]Привет\n[00:02.00]hello",Synced:true,Score:1}
	return model.Result{Best:&c,Candidates:[]model.Candidate{c}},nil
}
func TestBackwardCompatibilityAndJSON(t *testing.T){
	h:=New(finder{},262144)
	params:=url.Values{"artist":{"Demo"},"title":{"Night"},"album":{"Album"}}
	req:=httptest.NewRequest(http.MethodGet,"http://127.0.0.1/lyrics?"+params.Encode(),nil)
	rec:=httptest.NewRecorder();h.ServeHTTP(rec,req)
	body:=rec.Body.String()
	if rec.Code!=200{t.Fatalf("status %d: %s",rec.Code,body)}
	for _,marker:=range []string{"AIMPLB-ARTIST-BEGIN","AIMPLB-TITLE-END",
		"AIMPLB-LYRICS-BEGIN","AIMPLB-LYRICS-END","AIMPLB-LINEBREAK-7F3C"}{
		if !strings.Contains(body,marker){t.Fatalf("missing %s",marker)}
	}
	req=httptest.NewRequest("GET","http://127.0.0.1/api/v1/search?"+params.Encode(),nil)
	rec=httptest.NewRecorder();h.ServeHTTP(rec,req)
	var result model.Result
	if rec.Code!=200||json.Unmarshal(rec.Body.Bytes(),&result)!=nil||result.Best==nil {
		t.Fatalf("json search: %d: %s",rec.Code,rec.Body)}
	req=httptest.NewRequest("GET","http://127.0.0.1/lyrics?artist=Demo",nil)
	rec=httptest.NewRecorder();h.ServeHTTP(rec,req)
	if rec.Code!=400{t.Fatal("missing title should be 400")}
	req=httptest.NewRequest("GET","http://evil.example/lyrics?"+params.Encode(),nil)
	rec=httptest.NewRecorder();h.ServeHTTP(rec,req)
	if rec.Code!=403{t.Fatal("DNS rebinding Host not rejected")}
}
