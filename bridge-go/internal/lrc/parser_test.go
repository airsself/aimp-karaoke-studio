package lrc

import "testing"
func TestEnhancedAndOffset(t *testing.T){
	content:="[offset:+200]\n[ar:Demo]\n[00:01.00]<00:01.00>Hello <00:01.50>world\n[00:03.00][00:06.00]Дважды\n"
	d:=Parse(content,262144)
	if !d.Synced||len(d.Lines)!=3||d.OffsetMS!=200{
		t.Fatalf("bad parsed doc: %+v",d)}
	if d.Lines[0].AtMS!=1200||len(d.Lines[0].Words)!=2||d.Lines[0].Words[1].AtMS!=1700{
		t.Fatalf("lost word timing: %+v",d.Lines[0])}
	if d.Lines[1].AtMS!=3200||d.Lines[2].AtMS!=6200{t.Fatalf("lost duplicate timestamps: %+v",d)}
	plain:=Parse("hello\nworld\n",100)
	if plain.Synced||len(plain.Lines)!=2{t.Fatal("plain classification failed")}
	if d:=Parse(content,12);len(d.Lines)!=0{t.Fatal("limit bypassed")}
}
