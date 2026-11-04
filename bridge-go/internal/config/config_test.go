package config

import ("os";"path/filepath";"testing")
func TestDefaultAndLoad(t *testing.T){
 c:=Default()
 if err:=c.Validate();err!=nil{t.Fatal(err)}
 c.Listen="0.0.0.0:17321"
 if c.Validate()==nil{t.Fatal("non-loopback listener accepted")}
 c=Default();c.MinMatchScore=.99;c.EarlyAcceptScore=.94
 if c.Validate()==nil{t.Fatal("reversed scoring thresholds accepted")}
	dir:=t.TempDir()
	path:=filepath.Join(dir,"config.json")
	if err:=os.WriteFile(path,[]byte(`{"listen":"127.0.0.1:17321","provider_order":["lrclib"],"request_timeout_seconds":5}`),0600);err!=nil{t.Fatal(err)}
	loaded,err:=Load(path);if err!=nil{t.Fatal(err)}
	if loaded.RequestTimeoutSeconds!=5||!loaded.LRCLIBEnabled{t.Fatal("default overlay failed")}
}
