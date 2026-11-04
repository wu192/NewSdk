#!/usr/bin/env python3
"""Small schema-derived Joypad MOD and independent component input matrix.

Runtime IDs/sizes below were transcribed from original XmlCompiler polymorphic
wrappers in evidence/xml-source/parts/part-0149.cs.txt and part-0150.cs.txt;
the input field descriptions come from the unchanged SDK schemas, not recovery.
"""
import argparse
import copy
import json
from pathlib import Path
import xml.etree.ElementTree as ET

XS = "{http://www.w3.org/2001/XMLSchema}"
NS = "uri:ea.com:eala:asset"
PREFIX = "JoypadCommandBarButtonTemplateData"
ET.register_namespace("", NS)
VARIANTS = {
    "PlaceReadyStructure": (2129834919, 4), "ActivateHotSpot": (2610528252, 8),
    "ProductionQueueType": (3496866284, 12), "PlayerUpgrade": (740107052, 12),
    "UnitAbility": (4122740596, 8), "ToggleSpecialPower": (2278403426, 16),
    "OpenUnitsMenu": (510951828, 8), "SingleState": (3819920017, 8),
    "TargetedSpecialPower": (2577768761, 24), "AutoControlGroup": (3302104001, 120),
    "OpenPlayerTechStore": (3429542181, 8), "PlayerControlGroup": (2833918550, 12),
    "OpenCoopCommunicator": (3303154823, 8), "BaseSpecialPower": (3432035804, 8),
    "PlayerPower": (3057934633, 12), "ProduceStructureBase": (3450801933, 12),
    "SetStance": (325685730, 12), "Unpack": (313940736, 12),
    "PlayerMultipleTargetsTargetedPower": (3938890407, 48),
    "AIStrategicDirective": (1975379790, 12), "OpenControlGroups": (2638435896, 8),
    "OpenPlayerPowers": (1938484980, 8), "OpenUnitAbilities": (1810871923, 8),
    "RedAlert": (3304230519, 8), "OpenStances": (2778564342, 16),
    "PlayerMultiplePowersTargetedPower": (263057076, 40),
    "MultiplePowersTargetedSpecialPower": (2389969100, 40),
    "PlayerTargetedPower": (2969956408, 24), "ObjectUpgrade": (938329078, 12),
    "EnterCommandMode": (560773247, 12), "Upgrade": (2091051053, 12),
    "AssembleStructure": (3272079493, 12), "Evacuate": (2916649698, 12),
    "SpecialPower": (3804701620, 12),
}
ROOTS = {"JoypadCommandBarButtonTemplate": 8, "JoypadCommandBarTemplate": 24,
         "UIJoypadCommandBarButtonBuild": 12, "UIJoypadCommandBarSingleUnit": 20}
for _name in ("ControlGroups", "CoopCommunicator", "HomogenousGroup", "MixedGroup",
              "PlayerPowers", "Stances", "TopMenu"):
    ROOTS["UIJoypadCommandBar" + _name] = 8
REFS = ["", "R", "R\\0", "R\\7", "R\\-1", "R\\2147483647", "R\\2147483648",
        "R\\-2147483648", "R\\4294967295", "R\\99999999999999999999",
        "R\\ +17junk", "R\\garbage", "R\\2\\3", "MixedCase", "资源\\7"]
NUMBERS = ["0", "-0", "1", "-1", "12.25", "1e-7", "16777217", "2d1", "0x10",
           "garbage", "", "25.75junk", "25,5", "2147483648", "-2147483649",
           "4294967295", "4294967296", "1e400"]


class Schema:
    def __init__(self, root):
        self.types = {}
        for path in sorted(root.rglob("*.xsd")):
            for node in ET.parse(path).getroot():
                if node.tag in (XS + "complexType", XS + "simpleType") and node.get("name"):
                    self.types[node.get("name")] = node

    def fields(self, name):
        if name in ("BaseAssetType", "BaseInheritableAsset"):
            return []
        node = self.types.get(name)
        if node is None:
            return []
        extension = node.find(XS + "complexContent/" + XS + "extension")
        fields = self.fields(extension.get("base")) if extension is not None else []
        owner = extension if extension is not None else node
        for field in owner.iter():
            if field.tag in (XS + "attribute", XS + "element") and field.get("name"):
                # Keep the most derived repeated declaration only.
                fields = [x for x in fields if x.get("name") != field.get("name")]
                fields.append(field)
        return fields

    def enums(self, name):
        node = self.types.get(name)
        if node is None:
            return []
        restriction = node.find(XS + "restriction")
        if restriction is not None:
            enums = [n.get("value") for n in restriction.findall(XS + "enumeration")]
            return enums or self.enums(restriction.get("base"))
        listing = node.find(XS + "list")
        return self.enums(listing.get("itemType")) if listing is not None else []

    def value(self, name, sdk=False):
        enums = self.enums(name)
        if enums:
            return enums[0]
        if "Ref" in name or name in ("WeakReference", "AssetReference"):
            if not sdk:
                return "R\\7"
            if "ButtonSingleState" in name:
                return "JState"
            if "JoypadCommandBarButton" in name:
                return "JButtonPlaceReadyStructure"
            if "JoypadCommandBarTemplate" in name:
                return "JBar"
            if "LogicCommand" in name:
                return "JCommand"
            if "SpecialPower" in name:
                return "JPower"
            if "RadiusCursor" in name:
                return "JRadius"
            return "IgnoredWeakName"
        if "Bool" in name or name == "xs:boolean":
            return "true"
        if "Real" in name or name in ("xs:float", "xs:double"):
            return "2.5"
        if "Int" in name or name in ("xs:int", "xs:unsignedInt"):
            return "3"
        return "Test"

    def populate(self, element, name, sdk=False, depth=0):
        if depth > 5:
            raise ValueError("Unexpected schema recursion: " + name)
        for field in self.fields(name):
            kind = field.get("type", "xs:string")
            if field.tag == XS + "attribute":
                element.set(field.get("name"), self.value(kind, sdk))
            else:
                child = ET.SubElement(element, "{" + NS + "}" + field.get("name"))
                if self.fields(kind):
                    self.populate(child, kind, sdk, depth + 1)
                else:
                    child.text = self.value(kind, sdk)


def root(name):
    return ET.Element("{" + NS + "}" + name, {"id": "Oracle"})


def build(schema, out):
    cases = []

    def add(label, element, **layout):
        cases.append(dict(name=label, xml=ET.tostring(element, encoding="unicode"), **layout))

    def variations(label, base, owner_path, typename):
        for field in schema.fields(typename):
            name, kind = field.get("name"), field.get("type", "xs:string")
            if typename == "JoypadCommandBarButtonTemplate" and name == "Data":
                continue  # The numeric-dispatch cases below cover every branch.
            if field.tag == XS + "attribute":
                enums = schema.enums(kind)
                if enums:
                    values = enums if len(enums) < 40 else [enums[0], enums[-1]]
                    values += ["", "UNKNOWN", values[0].lower(), " " + values[0], values[0] + " "]
                elif "Ref" in kind or kind in ("WeakReference", "AssetReference"):
                    values = REFS
                elif "Int" in kind or "Real" in kind:
                    values = NUMBERS
                else:
                    values = ["", "Test", "MixedCase"]
                for i, value in enumerate(values):
                    element = copy.deepcopy(base)
                    target = element.find(owner_path) if owner_path else element
                    target.set(name, value)
                    add(label + "/" + name + "/" + str(i), element)
            else:
                for count in ([0, 1, 2, 17, 257] if field.get("maxOccurs") == "unbounded" else [0, 1]):
                    element = copy.deepcopy(base)
                    target = element.find(owner_path) if owner_path else element
                    for _ in range(count):
                        child = ET.SubElement(target, "{" + NS + "}" + name)
                        if schema.fields(kind):
                            schema.populate(child, kind)
                        else:
                            child.text = "R\\7"
                    add(label + "/" + name + "/count" + str(count), element)
                # Fields within nested list records, RGB and ObjectFilter.
                if schema.fields(kind):
                    element = copy.deepcopy(base)
                    target = element.find(owner_path) if owner_path else element
                    ET.SubElement(target, "{" + NS + "}" + name)
                    child_path = (owner_path + "/" if owner_path else "") + "{" + NS + "}" + name
                    variations(label + "/" + name, element, child_path, kind)

    for name, size in ROOTS.items():
        base = root(name)
        add("defaults/" + name, base, rootSize=size, exactSize=size)
        variations(name, base, "", name)
        if name != "JoypadCommandBarButtonTemplate":
            populated = root(name)
            schema.populate(populated, name)
            add("complete/" + name, populated)
    for name, (type_id, size) in VARIANTS.items():
        element = root("JoypadCommandBarButtonTemplate")
        data = ET.SubElement(element, "{" + NS + "}Data")
        payload = ET.SubElement(data, "{" + NS + "}" + name, {"TypeId": str(type_id)})
        add("variant-default/" + name, element, exactSize=8+size, payloadType=type_id, payloadSize=size)
        variations(name, element, "{" + NS + "}Data/{" + NS + "}" + name, PREFIX + name)
        full = copy.deepcopy(element)
        schema.populate(full.find("{" + NS + "}Data/{" + NS + "}" + name), PREFIX + name)
        add("variant-complete/" + name, full)
    (out / "cases.json").write_text(json.dumps(cases, ensure_ascii=False), encoding="utf-8")

    declaration = ET.Element("{" + NS + "}AssetDeclaration")
    for name, identifier in [("ButtonSingleStateData", "JState"), ("LogicCommand", "JCommand"),
                             ("SpecialPowerTemplate", "JPower"), ("RadiusCursor", "JRadius")]:
        node = ET.SubElement(declaration, "{" + NS + "}" + name, {"id": identifier})
        if name == "ButtonSingleStateData":
            ET.SubElement(node, "{" + NS + "}State", {"Title": "NAME:Joypad", "Description": "DESC:Joypad"})
        if name == "RadiusCursor":
            ET.SubElement(node, "{" + NS + "}DecalTemplate")
        if name == "SpecialPowerTemplate":
            node.set("TargetType", "NONE")
    choices = schema.types["JoypadCommandBarButtonTemplateDataRef"].find(XS + "choice")
    concrete = [node.get("name") for node in choices]
    for name in concrete:
        node = ET.SubElement(declaration, "{" + NS + "}JoypadCommandBarButtonTemplate",
                             {"id": "JButton" + name})
        data = ET.SubElement(node, "{" + NS + "}Data")
        payload = ET.SubElement(data, "{" + NS + "}" + name)
        schema.populate(payload, PREFIX + name, sdk=True)
    for name in ROOTS:
        if name == "JoypadCommandBarButtonTemplate":
            continue
        node = ET.SubElement(declaration, "{" + NS + "}" + name,
                             {"id": "JBar" if name == "JoypadCommandBarTemplate" else "J" + name})
        schema.populate(node, name, sdk=True)
    mod = out / "mod"
    (mod / "Data").mkdir(parents=True)
    ET.indent(declaration)
    ET.ElementTree(declaration).write(mod / "Data/Mod.xml", encoding="utf-8", xml_declaration=True)
    (out / "fixtures.json").write_text(json.dumps(dict(rootNames=list(ROOTS), variants=len(VARIANTS),
        componentCases=len(cases), sdkChoices=concrete, sdkAssets=len(declaration)), indent=2), encoding="utf-8")
    print(f"Joypad fixtures: {len(cases)} component cases, {len(declaration)} MOD assets, {len(concrete)} schema choices")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--sdk", type=Path, default=Path("SDK"))
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=False)
    build(Schema(args.sdk / "Schemas/xsd"), args.output)


if __name__ == "__main__":
    main()
