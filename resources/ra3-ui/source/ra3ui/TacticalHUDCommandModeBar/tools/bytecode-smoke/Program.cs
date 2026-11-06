if(args.Length!=1){Console.Error.WriteLine("Usage: RawSmoke <APT/CONST basename without extension>");return 2;}
string stem=Path.GetFullPath(args[0]);
byte[] apt=File.ReadAllBytes(stem+".apt"), constants=File.ReadAllBytes(stem+".const");
int checks=0;
void Equal(object value,object expected,string reason){if(!value.Equals(expected))throw new Exception(reason+": "+value+" != "+expected);checks++;}
Avm1Machine Start(bool defer){var vm=new Avm1Machine();vm.ConfigurePowerMocks(defer);vm.RunRawRoot(apt,constants);return vm;}
var vm=Start(false);
Equal(vm.Value("readyCount"),1,"Ready once");Equal(vm.CallGlobal("GetPowerButton"),"@button","GetPowerButton encodes button rather than root");Equal(vm.ButtonValue("_visible"),false,"initial hidden");Equal(vm.ButtonValue("isEnabled"),false,"initial disabled");
Equal(Convert.ToDouble(vm.ButtonValue("_x")),-40.0,"Power left of repair");
vm.InvokeGlobal("SetPowerModeState","1","1");Equal(vm.ButtonValue("isSelected"),true,"selected");Equal(vm.ButtonValue("_visible"),true,"supported visible");
vm.InvokeButton("OnPress");Equal(vm.Value("toggleCount"),0,"press is visual");Equal(vm.ButtonValue("stateLabel"),"_down","press feedback");
vm.InvokeButton("OnRelease");Equal(vm.Value("toggleCount"),1,"one release one Toggle");
vm.InvokeButton("OnRollOver");Equal(vm.HintVisible(),true,"hover tooltip");vm.InvokeButton("OnRollOut");Equal(vm.HintVisible(),false,"leave tooltip");
vm.InvokeGlobal("SetPowerModeState","0","0");vm.InvokeButton("OnRelease");Equal(vm.Value("toggleCount"),1,"disabled release rejected");Equal(vm.ButtonValue("_visible"),false,"hidden unavailable");
vm.InvokeGlobal("onUnload");Equal(vm.Value("unloadCount"),1,"matching root unload once");
vm=Start(true);Equal(vm.Value("readyCount"),0,"deferred frame not ready");vm.CompleteDeferred();Equal(vm.Value("readyCount"),1,"deferred init Ready once");
vm.InvokeGlobal("SetPowerModeState","0","1");vm.InvokeButton("OnRelease");Equal(vm.Value("toggleCount"),1,"deferred callback installed");
vm=Start(true);vm.InvokeGlobal("onUnload");Equal(vm.Value("readyCount"),0,"teardown before init");Equal(vm.Value("unloadCount"),1,"early unload bound root");
Console.WriteLine($"Raw APT/CONST bytecode smoke passed: {checks} checks; rendering/native calls mocked; no FLA/AS/compiler input.");
return 0;
