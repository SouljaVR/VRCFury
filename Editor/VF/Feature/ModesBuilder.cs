using System.Linq;
using UnityEditor;
using UnityEngine.UIElements;
using VF.Feature.Base;
using VF.Inspector;
using VF.Model.Feature;

namespace VF.Feature {

public class ModesBuilder : FeatureBuilder<Modes> {
    [FeatureBuilderAction]
    public void Apply() {
        var fx = GetFx();
        var physBoneResetter = CreatePhysBoneResetter(model.resetPhysbones, model.name);

        var layerName = model.name;
        var layer = fx.NewLayer(layerName);
        var off = layer.NewState("Off");
        if (physBoneResetter != null) off.Drives(physBoneResetter, true);
        var param = fx.NewInt(model.name, synced: true, saved: model.saved);
        manager.GetMenu().NewMenuToggle(model.name + "/Off", param, 0);
        var i = 1;
        foreach (var mode in model.modes) {
            var num = i++;
            var clip = LoadState(model.name+"_"+num, mode.state);
            var state = layer.NewState(""+num).WithAnimation(clip);
            if (physBoneResetter != null) state.Drives(physBoneResetter, true);
            if (model.securityEnabled && allFeaturesInRun.Any(f => f is SecurityLock)) {
                var paramSecuritySync = fx.NewBool("SecurityLockSync");
                state.TransitionsFromAny().When(param.IsEqualTo(num).And(paramSecuritySync.IsTrue()));
                state.TransitionsToExit().When(param.IsNotEqualTo(num));
                state.TransitionsToExit().When(paramSecuritySync.IsFalse());
            } else {
                state.TransitionsFromAny().When(param.IsEqualTo(num));
                state.TransitionsToExit().When(param.IsNotEqualTo(num));
            }
            manager.GetMenu().NewMenuToggle(model.name + "/Mode " + num, param, num);
        }
    }

    public override string GetEditorTitle() {
        return "Prop with Modes";
    }

    public override VisualElement CreateEditor(SerializedProperty prop) {
        return ToggleBuilder.CreateEditor(prop, content =>
            content.Add(VRCFuryEditorUtils.List(prop.FindPropertyRelative("modes"),
                renderElement: (i,e) => VRCFuryStateEditor.render(e.FindPropertyRelative("state"), "Mode " + (i+1)))));
    }
}

}
