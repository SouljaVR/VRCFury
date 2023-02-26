using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.ScriptableObjects;
using VF.Builder;
using VF.Feature.Base;
using VF.Inspector;
using VF.Model;
using VF.Model.Feature;
using VF.Model.StateAction;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.Contact.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;
using Toggle = VF.Model.Feature.Toggle;

namespace VF.Feature {

    public class FullControllerBuilder : FeatureBuilder<FullController> {

        [FeatureBuilderAction]
        public void Apply() {
            var baseObject = model.rootObjOverride;
            if (baseObject == null) {
                baseObject = featureBaseObject;
            }

            var rewrittenParams = new HashSet<string>();
            Func<string,string> rewriteParam = name => {
                if (string.IsNullOrWhiteSpace(name)) return name;
                if (VRChatGlobalParams.Contains(name)) return name;
                if (model.allNonsyncedAreGlobal) {
                    var synced = model.prms.Any(p => p.parameters.parameters.Any(param => param.name == name));
                    if (!synced) return name;
                }
                if (model.globalParams.Contains(name)) {
                    return name;
                }
                if (model.globalParams.Contains("*")) {
                    return name;
                }

                rewrittenParams.Add(name);
                return ControllerManager.NewParamName(name, uniqueModelNum);
            };

            var toggleIsInt = false;
            foreach (var p in model.prms) {
                if (p.parameters == null) continue;
                foreach (var param in p.parameters.parameters) {
                    if (param.name == model.toggleParam && param.valueType == VRCExpressionParameters.ValueType.Int)
                        toggleIsInt = true;
                    if (string.IsNullOrWhiteSpace(param.name)) continue;
                    var newParam = new VRCExpressionParameters.Parameter {
                        name = rewriteParam(param.name),
                        valueType = param.valueType,
                        saved = param.saved && !model.ignoreSaved,
                        defaultValue = param.defaultValue
                    };
                    manager.GetParams().addSyncedParam(newParam);
                }
            }

            var rewrittenClips = new Dictionary<AnimationClip, AnimationClip>();

            foreach (var c in model.controllers) {
                var type = c.type;
                var source = c.controller as AnimatorController;
                if (source == null) continue;
                
                AnimationClip RewriteClip(AnimationClip from) {
                    if (from == null) {
                        return null;
                    }
                    if (rewrittenClips.ContainsKey(from)) return rewrittenClips[from];
                    AnimationClip rewritten;
                    if (AssetDatabase.GetAssetPath(from).Contains("/proxy_")) {
                        rewritten = from;
                    } else {
                        rewritten = manager.GetClipStorage().NewClip(from.name);
                        ClipCopier.Copy(
                            from,
                            rewritten,
                            fromObj: baseObject,
                            fromRoot: avatarObject,
                            removePrefixes: model.removePrefixes,
                            addPrefix: model.addPrefix,
                            rootBindingsApplyToAvatar: model.rootBindingsApplyToAvatar,
                            rewriteParam: rewriteParam
                        );
                    }

                    rewrittenClips[from] = rewritten;
                    return rewritten;
                }
                
                BlendTree NewBlendTree(string name) {
                    return manager.GetClipStorage().NewBlendTree(name);
                }

                var targetController = manager.GetController(type);
                if (type == VRCAvatarDescriptor.AnimLayerType.Gesture && source.layers.Length > 0) {
                    targetController.UnionBaseMask(source.layers[0].avatarMask);
                }
                var merger = new ControllerMerger(
                    layerName => "FC - " + layerName,
                    param => rewriteParam(param),
                    RewriteClip,
                    NewBlendTree
                );
                merger.Merge(source, targetController);
            }

            foreach (var m in model.menus) {
                if (m.menu == null) continue;
                var prefix = MenuManager.SplitPath(m.prefix);
                manager.GetMenu().MergeMenu(prefix, m.menu, rewriteParam);
            }
            
            foreach (var receiver in baseObject.GetComponentsInChildren<VRCContactReceiver>(true)) {
                if (rewrittenParams.Contains(receiver.parameter)) {
                    receiver.parameter = rewriteParam(receiver.parameter);
                }
            }
            foreach (var physbone in baseObject.GetComponentsInChildren<VRCPhysBone>(true)) {
                if (rewrittenParams.Contains(physbone.parameter + "_IsGrabbed") || rewrittenParams.Contains(physbone.parameter + "_Angle") || rewrittenParams.Contains(physbone.parameter + "_Stretch")) {
                    physbone.parameter = rewriteParam(physbone.parameter);
                }
            }

            if (!string.IsNullOrWhiteSpace(model.toggleParam)) {
                addOtherFeature(new ObjectState {
                    states = {
                        new ObjectState.ObjState {
                            action = ObjectState.Action.DEACTIVATE,
                            obj = baseObject
                        }
                    }
                });
                var toggleParam = rewriteParam(model.toggleParam);
                addOtherFeature(new Toggle {
                    name = toggleParam,
                    state = new State {
                        actions = { new ObjectToggleAction { obj = baseObject } }
                    },
                    securityEnabled = model.useSecurityForToggle,
                    addMenuItem = false,
                    usePrefixOnParam = false,
                    paramOverride = toggleParam,
                    useInt = toggleIsInt
                });
            }
        }

        public override string GetEditorTitle() {
            return "Full Controller";
        }

        public override VisualElement CreateEditor(SerializedProperty prop) {
            var content = new VisualElement();
            
            content.Add(VRCFuryEditorUtils.Info(
                "This feature will merge the given controller / menu / parameters into the avatar" +
                " during the upload process."));
            
            content.Add(VRCFuryEditorUtils.WrappedLabel("Controllers:"));
            content.Add(VRCFuryEditorUtils.List(prop.FindPropertyRelative("controllers"),
                (i, el) => {
                    var wrapper = new VisualElement();
                    wrapper.style.flexDirection = FlexDirection.Row;
                    var a = VRCFuryEditorUtils.Prop(el.FindPropertyRelative("controller"));
                    a.style.flexBasis = 0;
                    a.style.flexGrow = 1;
                    wrapper.Add(a);
                    var b = VRCFuryEditorUtils.Prop(el.FindPropertyRelative("type"));
                    b.style.flexBasis = 0;
                    b.style.flexGrow = 1;
                    wrapper.Add(b);
                    return wrapper;
                }));

            content.Add(VRCFuryEditorUtils.WrappedLabel("Menus + Path Prefix:"));
            content.Add(VRCFuryEditorUtils.WrappedLabel("(If prefix is left empty, menu will be merged into avatar's root menu)"));
            content.Add(VRCFuryEditorUtils.List(prop.FindPropertyRelative("menus"),
                (i, el) => {
                    var wrapper = new VisualElement();
                    wrapper.style.flexDirection = FlexDirection.Row;
                    var a = VRCFuryEditorUtils.Prop(el.FindPropertyRelative("menu"));
                    a.style.flexBasis = 0;
                    a.style.flexGrow = 1;
                    wrapper.Add(a);
                    var b = VRCFuryEditorUtils.Prop(el.FindPropertyRelative("prefix"));
                    b.style.flexBasis = 0;
                    b.style.flexGrow = 1;
                    wrapper.Add(b);
                    return wrapper;
                }));
            
            content.Add(VRCFuryEditorUtils.WrappedLabel("Parameters:"));
            content.Add(VRCFuryEditorUtils.List(prop.FindPropertyRelative("prms"),
                (i, el) => VRCFuryEditorUtils.Prop(el.FindPropertyRelative("parameters"))));
            
            content.Add(VRCFuryEditorUtils.WrappedLabel("Global Parameters:"));
            content.Add(VRCFuryEditorUtils.WrappedLabel(
                "Parameters in this list will have their name kept as is, allowing you to interact with " +
                "parameters in the avatar itself or other instances of the prop. Note that VRChat global " +
                "parameters (such as gestures) are included by default."));
            content.Add(VRCFuryEditorUtils.List(prop.FindPropertyRelative("globalParams")));
            
            content.Add(VRCFuryEditorUtils.WrappedLabel("Remove prefixes from clips:"));
            content.Add(VRCFuryEditorUtils.WrappedLabel(
                "Strings in this list will be removed from the start of every animated key, useful if the animations" +
                " in the controller were originally written to be based from the avatar root, " +
                "but you are now trying to use as a VRCFury prop."));
            content.Add(VRCFuryEditorUtils.List(prop.FindPropertyRelative("removePrefixes")));

            var adv = new Foldout {
                text = "Advanced Options",
                value = false
            };
            
            adv.Add(VRCFuryEditorUtils.Prop(prop.FindPropertyRelative("allNonsyncedAreGlobal"), "Make all unsynced params global (Legacy mode)"));
            adv.Add(VRCFuryEditorUtils.Prop(prop.FindPropertyRelative("ignoreSaved"), "Force all synced parameters to be un-saved"));
            adv.Add(VRCFuryEditorUtils.Prop(prop.FindPropertyRelative("rootObjOverride"), "Root object override"));
            adv.Add(VRCFuryEditorUtils.Prop(prop.FindPropertyRelative("rootBindingsApplyToAvatar"), "Root bindings always apply to avatar (Basically only for gogoloco)"));
            adv.Add(VRCFuryEditorUtils.WrappedLabel(
                "Parameter name for prop toggling. If set, this entire prop will be de-activated whenever" +
                " this boolean parameter within the Full Controller is false."));
            adv.Add(VRCFuryEditorUtils.Prop(prop.FindPropertyRelative("toggleParam")));
            adv.Add(VRCFuryEditorUtils.Prop(prop.FindPropertyRelative("useSecurityForToggle"), "Use security for toggle"));
            adv.Add(VRCFuryEditorUtils.Prop(prop.FindPropertyRelative("addPrefix"),
                "Add prefix to clips"));

            content.Add(adv);

            return content;
        }
        
        private static HashSet<string> VRChatGlobalParams = new HashSet<string> {
            "IsLocal",
            "Viseme",
            "Voice",
            "GestureLeft",
            "GestureRight",
            "GestureLeftWeight",
            "GestureRightWeight",
            "AngularY",
            "VelocityX",
            "VelocityY",
            "VelocityZ",
            "Upright",
            "Grounded",
            "Seated",
            "AFK",
            "TrackingType",
            "VRMode",
            "MuteSelf",
            "InStation",
            "AvatarVersion",
            "GroundProximity"
        };
    }

}