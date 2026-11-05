// Local HUD protocol shared with EnhancerCorona's PowerToggle extension.
function GetPowerButton()
{
   return Pointer.encode(powerButton);
}
function UpdatePowerButtonVisuals()
{
   if(powerButton == undefined)
   {
      return undefined;
   }
   var tint = 3425881;
   if(powerButton.isSelected)
   {
      tint = 13537066;
   }
   if(powerButton.stateLabel == "_over")
   {
      tint = powerButton.isSelected ? 15914563 : 5795737;
   }
   if(powerButton.stateLabel == "_down")
   {
      tint = 2177331;
   }
   powerButton.panelColor.setRGB(tint);
   powerButton._alpha = powerButton.isEnabled ? 100 : 35;
}
function ShowPowerButtonHelp()
{
   powerHintBackground._visible = powerButton.isEnabled;
   powerHintText._visible = powerButton.isEnabled;
}
function HidePowerButtonHelp()
{
   // HUD teardown can precede the deferred button initialization.
   if(powerHintBackground != undefined)
   {
      powerHintBackground._visible = false;
   }
   if(powerHintText != undefined)
   {
      powerHintText._visible = false;
   }
}
function SetPowerModeState(activeArg, enabledArg)
{
   if(powerButton == undefined)
   {
      return undefined;
   }
   var canUse = Number(enabledArg) != 0;
   powerButton.SetSelected(canUse && Number(activeArg) != 0 ? "1" : "0");
   powerButton.SetEnabled(canUse ? "1" : "0");
   powerButton.SetVisibility(canUse ? "1" : "0");
   if(!canUse)
   {
      HidePowerButtonHelp();
   }
   UpdatePowerButtonVisuals();
}
function InitializePowerButton()
{
   // The init object uses the same attachMovie contract as RenderingSurface.
   powerButton = attachMovie("EnhancerPowerButton","powerButton",200,
      {buttonClipName:"RA3CommandModeButtonClip",imageClipName:"",backgroundClipName:"",helpSite:helpSite,_x:-40,_y:0,_visible:false});
   // attachMovie may defer its frame script until the next APT frame.
   // Install instance callbacks only after the shared controller initialized.
   if(powerButton.initialized)
   {
      FinishPowerButtonInitialization();
   }
   else
   {
      onEnterFrame = function()
      {
         if(this.powerButton.initialized)
         {
            delete this.onEnterFrame;
            this.FinishPowerButtonInitialization();
         }
      };
   }
}
function FinishPowerButtonInitialization()
{
   var panel = powerButton.backgroundStage.attachMovie("EnhancerPowerSurface","panel",1);
   panel._width = 37;
   panel._height = 22;
   powerButton.panelColor = new Color(panel);
   var icon = powerButton.imageStage.attachMovie("EnhancerPowerIcon","powerIcon",1);
   icon._x = 7;
   icon._y = 1;

   // Reuse an existing text-field symbol; no dynamic drawing/text APIs needed.
   powerHintBackground = attachMovie("EnhancerPowerSurface","powerHintBackground",201,
      {_x:-40,_y:27,_width:238,_height:25,_visible:false});
   var hintColor = new Color(powerHintBackground);
   hintColor.setRGB(1053464);
   powerHintText = attachMovie("EnhancerPowerHintText","powerHintText",202,
      {_x:-37,_y:27,_visible:false});
   powerHintText.field._width = 232;
   powerHintText.field.text = "Power off / restore";
   powerHintText.text = "Power off / restore";

   // Only this instance changes its event callbacks. Repair/Sell stay intact.
   powerButton.SetState = function(newStateLabel)
   {
      this.stateLabel = newStateLabel;
      this.buttonStage.clip.gotoAndPlay(newStateLabel);
      this._parent.UpdatePowerButtonVisuals();
   };
   powerButton.OnPress = function()
   {
      if(this.isEnabled)
      {
         this.SetState("_down");
      }
   };
   powerButton.OnRelease = function()
   {
      if(this.isEnabled)
      {
         this.SetState("_over");
         fscommand("CallGameFunction","%PowerToggle:Toggle");
      }
   };
   powerButton.OnRollOver = function()
   {
      this.isOver = true;
      if(this.isEnabled)
      {
         this.SetState("_over");
         this._parent.ShowPowerButtonHelp();
      }
   };
   powerButton.OnRollOut = function()
   {
      this.isOver = false;
      if(this.isEnabled)
      {
         this.SetState("_up");
      }
      this._parent.HidePowerButtonHelp();
   };
   SetPowerModeState("0","0");
   // Native registers support before making this optional control visible.
   fscommand("CallGameFunction","%PowerToggle:Ready," + Pointer.encode(this));
}
function UnloadPowerButton()
{
   delete onEnterFrame;
   fscommand("CallGameFunction","%PowerToggle:Unload," + Pointer.encode(this));
   HidePowerButtonHelp();
   powerHintText.removeMovieClip();
   powerHintBackground.removeMovieClip();
   powerButton.removeMovieClip();
   delete powerHintText;
   delete powerHintBackground;
   delete powerButton;
}
