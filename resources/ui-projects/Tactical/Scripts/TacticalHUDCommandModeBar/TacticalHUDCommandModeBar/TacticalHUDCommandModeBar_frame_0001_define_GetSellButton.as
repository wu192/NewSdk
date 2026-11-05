#include "../powerButton/PowerModeControl.as"

function GetSellButton()
{
   return Pointer.encode(sellButton);
}
function GetRepairButton()
{
   return Pointer.encode(repairButton);
}
function GetPauseButton()
{
   return Pointer.encode(pauseButton);
}
function onUnload()
{
   UnloadPowerButton();
   delete GetPowerButton;
   delete SetPowerModeState;
   delete InitializePowerButton;
   delete FinishPowerButtonInitialization;
   delete UnloadPowerButton;
   delete UpdatePowerButtonVisuals;
   delete ShowPowerButtonHelp;
   delete HidePowerButtonHelp;
   delete GetSellButton;
   delete GetRepairButton;
   delete GetPauseButton;
   delete onUnload;
}
if(!initialized)
{
   sellButton.helpSite = helpSite;
   repairButton.helpSite = helpSite;
   pauseButton.helpSite = helpSite;
   initialized = true;
   fscommand(qualifyName(this,"OnInitialized"),"");
   InitializePowerButton();
}
stop();
