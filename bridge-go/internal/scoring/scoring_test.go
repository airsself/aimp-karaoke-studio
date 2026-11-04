package scoring

import("testing";"github.com/airsself/aimp-karaoke-studio/bridge-go/internal/model")
func TestScoreAndUnicode(t *testing.T) {
	want:=model.Track{Artist:"Björk",Title:"Jóga",DurationSeconds:240}
	got:=want
	if v:=Score(want,got);v!=1{t.Fatalf("exact=%v",v)}
	got.DurationSeconds=300
	if v:=Score(want,got);v!=.75{t.Fatalf("duration penalty=%v",v)}
	got=want;got.Artist="Other"
	if v:=Score(want,got);v>=.78{t.Fatalf("wrong artist unexpectedly accepted: %v",v)}
	if Normalize("CaFe\u0301 - 你好")==""{t.Fatal("unicode erased")}
	if Normalize("We're Here")!="werehere"{t.Fatal("punctuation normalization")}
}
