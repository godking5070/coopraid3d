"""Read-only source-to-textured Eris validation after fresh reopen.

Only slot 0 on Head/body and Hair may change, to Eris_ConceptBody/Hair.
All mesh data, UVs, polygon assignments, skinning, bones and Actions stay exact.
The optional manifest declares materials/maps by object and material input role.
"""
import argparse
import hashlib
import json
import math
from pathlib import Path
import sys

import bpy
import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "Animation"))
from validate_animated_character import action_data, component_for, digest, file_sha, material_data

EXPECTED_MATERIALS = {"Head": "Eris_ConceptBody", "Hair": "Eris_ConceptHair"}
ROLES = {"BaseColor": "Base Color", "Roughness": "Roughness", "Metallic": "Metallic", "Normal": "Normal"}


def geometry_signature(obj):
    mesh = obj.data
    shape_keys = []
    if mesh.shape_keys:
        shape_keys = [(key.name, key.value, [list(point.co) for point in key.data])
                      for key in mesh.shape_keys.key_blocks]
    return digest({"vertices": [list(vertex.co) for vertex in mesh.vertices],
        "edges": [list(edge.vertices) for edge in mesh.edges],
        "loops": [loop.vertex_index for loop in mesh.loops],
        "faces": [(list(face.vertices), face.use_smooth) for face in mesh.polygons],
        "uv": [(layer.name, layer.active_render, [list(loop.uv) for loop in layer.data])
               for layer in mesh.uv_layers], "active_uv": mesh.uv_layers.active_index,
        "attributes": [(attribute.name, attribute.data_type, attribute.domain) for attribute in mesh.attributes],
        "shape_keys": shape_keys})


def modifier_signature(obj):
    records = []
    for mod in obj.modifiers:
        item = {"name": mod.name, "type": mod.type, "viewport": mod.show_viewport, "render": mod.show_render}
        for name in ("object", "vertex_group", "invert_vertex_group", "use_vertex_groups",
                     "use_bone_envelopes", "use_deform_preserve_volume"):
            if hasattr(mod, name):
                value = getattr(mod, name)
                item[name] = value.name if hasattr(value, "name") else value
        records.append(item)
    return digest(records)


def image_identity(image):
    payload = bytes(image.packed_file.data) if image.packed_file else None
    path = Path(bpy.path.abspath(image.filepath)) if image.filepath else None
    if payload is None and path and path.is_file():
        payload = path.read_bytes()
    return {"source": image.source, "type": image.type, "size": list(image.size),
            "colorspace": image.colorspace_settings.name, "alpha_mode": image.alpha_mode,
            "payload_sha256": hashlib.sha256(payload).hexdigest() if payload is not None else None,
            "packed": bool(image.packed_file)}


def capture():
    bpy.context.view_layer.update()
    armature = bpy.data.objects["Armature"]
    meshes = {}
    for obj in bpy.context.scene.objects:
        if obj.type != "MESH":
            continue
        names = {group.index: group.name for group in obj.vertex_groups}
        meshes[obj.name] = {"geometry": geometry_signature(obj), "modifiers": modifier_signature(obj),
            "matrix_world": [list(row) for row in obj.matrix_world],
            "matrix_parent_inverse": [list(row) for row in obj.matrix_parent_inverse],
            "parent": obj.parent.name if obj.parent else None,
            "groups": [(group.name, group.lock_weight) for group in obj.vertex_groups],
            "weights": digest([{names[group.group]: float(group.weight) for group in vertex.groups}
                                for vertex in obj.data.vertices]),
            "slots": [slot.material.name if slot.material else None for slot in obj.material_slots],
            "polygon_material_indices": [face.material_index for face in obj.data.polygons],
            "vertices": len(obj.data.vertices)}
    animation = armature.animation_data
    return {"meshes": meshes,
        "bones": {bone.name: {"matrix": [list(row) for row in bone.matrix_local],
                    "head": list(bone.head_local), "tail": list(bone.tail_local),
                    "parent": bone.parent.name if bone.parent else None, "deform": bone.use_deform}
                  for bone in armature.data.bones},
        "armature_world": [list(row) for row in armature.matrix_world],
        "pose_position": armature.data.pose_position,
        "pose_bases": {bone.name: [list(row) for row in bone.matrix_basis] for bone in armature.pose.bones},
        "actions": {action.name: action_data(action) for action in bpy.data.actions},
        "animation": {"action": animation.action.name if animation and animation.action else None,
            "slot": animation.action_slot.identifier if animation and animation.action_slot else None,
            "use_nla": animation.use_nla if animation else None,
            "tracks": [(track.name, track.mute, [(strip.name, strip.action.name if strip.action else None,
                       strip.frame_start, strip.frame_end) for strip in track.strips])
                       for track in animation.nla_tracks] if animation else []},
        "playback": {"fps": bpy.context.scene.render.fps, "base": bpy.context.scene.render.fps_base,
                     "range": [bpy.context.scene.frame_start, bpy.context.scene.frame_end],
                     "frame": bpy.context.scene.frame_current},
        "materials": {material.name: material_data(material) for material in bpy.data.materials},
        "images": {image.name: image_identity(image) for image in bpy.data.images if image.source == "FILE"}}


def upstream_images(socket):
    found, visited = {}, set()
    def visit(current):
        for link in current.links:
            node = link.from_node
            if node.as_pointer() in visited:
                continue
            visited.add(node.as_pointer())
            if node.type == "TEX_IMAGE" and node.image:
                found[node.image.name] = node.image
            else:
                for input_socket in node.inputs:
                    if input_socket.is_linked:
                        visit(input_socket)
    visit(socket)
    return list(found.values())


def pixel_summary(image):
    # Scan one image at a time; freeing decoded buffers avoids retaining eight
    # simultaneous 4096 RGBA float buffers in this background QA process.
    values = np.empty(len(image.pixels), dtype=np.float32)
    image.pixels.foreach_get(values)
    count = int(values.size)
    finite = bool(count and np.isfinite(values).all())
    result = {"values": count, "finite": finite,
              "minimum": float(values.min()) if count else None,
              "maximum": float(values.max()) if count else None}
    if count and count % 4 == 0:
        pixels = values.reshape((-1, 4))
        result["channel_min"] = pixels.min(axis=0).tolist()
        result["channel_max"] = pixels.max(axis=0).tolist()
        result["rgb_grayscale_max_error"] = float(np.abs(pixels[:, :3] - pixels[:, :1]).max())
    del values
    image.buffers_free()
    return result


def semantic_color_validation(source_path, final_path, report_path):
    """Sample actual surface UV interiors, excluding unused atlas padding.

    This checks that the warm/pink concept recolor reached real hair/cloth,
    and that protected skin and hair strands did not acquire metallic foil.
    It supplements the separate structural/image integrity validation.
    """
    source_path, final_path, report_path = map(lambda path: Path(path).resolve(),
                                               (source_path, final_path, report_path))
    source_hash, final_hash = file_sha(source_path), file_sha(final_path)
    palette = {"navy": "#41496D", "hair_tip": "#F6BDBA"}
    spec_path = final_path.parent / "texture-spec.json"
    if spec_path.is_file():
        palette.update(json.loads(spec_path.read_text(encoding="utf-8")).get("palette", {}))
    target_colors = {name: np.asarray([int(palette[key].lstrip("#")[index:index + 2], 16) / 255
                                       for index in (0, 2, 4)])
                     for name, key in (("BodyNavy", "navy"), ("HairPinkTips", "hair_tip"))}
    bpy.ops.wm.open_mainfile(filepath=str(source_path), load_ui=False, use_scripts=False)
    body, hair = bpy.data.objects["Head"], bpy.data.objects["Hair"]
    source_image = next(node.image for slot in body.material_slots if slot.material and slot.material.node_tree
                        for node in slot.material.node_tree.nodes if node.type == "TEX_IMAGE" and node.image)

    def pixels(image):
        result = np.empty(len(image.pixels), dtype=np.float32)
        image.pixels.foreach_get(result)
        return result.reshape((-1, 4))

    def sample(array, dimensions, uv):
        width, height = dimensions
        x, y = np.clip(uv[:, 0] * width - .5, 0, width - 1), np.clip(uv[:, 1] * height - .5, 0, height - 1)
        x0, y0 = np.floor(x).astype(int), np.floor(y).astype(int)
        x1, y1 = np.minimum(x0 + 1, width - 1), np.minimum(y0 + 1, height - 1)
        tx, ty = (x - x0)[:, None], (y - y0)[:, None]
        return ((array[y0 * width + x0] * (1 - tx) + array[y0 * width + x1] * tx) * (1 - ty)
                + (array[y1 * width + x0] * (1 - tx) + array[y1 * width + x1] * tx) * ty)[:, :3]

    source_pixels, source_dimensions = pixels(source_image), tuple(source_image.size)
    regions = {}
    protected = {name: component_for(body.data, seed) for name, seed in
                 (("Face", 509), ("LeftHand", 5798), ("RightHand", 750))}
    protected_union = set().union(*protected.values())
    ornaments = set().union(*(component_for(hair.data, seed) for seed in (1405, 1369, 1867, 10388, 10373, 1861)))
    largest_hair_island = component_for(hair.data, 116)
    for obj in (body, hair):
        faces = list(obj.data.polygons)
        uv = np.asarray([np.mean([obj.data.uv_layers.active.data[index].uv[:] for index in face.loop_indices], axis=0)
                         for face in faces], dtype=np.float64)
        z = np.asarray([sum((obj.matrix_world @ obj.data.vertices[index].co).z for index in face.vertices) / len(face.vertices)
                        for face in faces])
        color = sample(source_pixels, source_dimensions, uv)
        r, g, b = color.T
        vmax, vmin = color.max(axis=1), color.min(axis=1)
        saturation = (vmax - vmin) / np.maximum(vmax, 1e-8)
        gold = (r >= g) & (g >= b) & (saturation > .30) & (r > .45) & ((g - b) / np.maximum(r - b, 1e-8) > .48)
        selections = {}
        if obj.name == "Head":
            is_protected = np.asarray([any(index in protected_union for index in face.vertices) for face in faces])
            selections["BodyNavy"] = ~is_protected & (b > r + .025) & (vmax < .62) & (saturation > .18)
            selections["BodyGold"] = ~is_protected & gold
            for name, vertices in protected.items():
                mask = np.asarray([all(index in vertices for index in face.vertices) for face in faces])
                selections["Skin" + name] = mask & (z > .66) if name == "Face" else mask
        else:
            names = {group.index: group.name for group in obj.vertex_groups}
            weights = np.asarray([sum(group.weight for group in vertex.groups if names[group.group].startswith("Hair_"))
                                  for vertex in obj.data.vertices])
            hair_weight = np.asarray([float(weights[list(face.vertices)].mean()) for face in faces])
            strands = hair_weight > .5
            warm = (r > b + .015) & (r > .45)
            low, high = np.quantile(z[strands], [.3, .7])
            selections["HairPinkTips"] = strands & warm & (z <= low)
            selections["HairWarmRoots"] = strands & warm & (z >= high)
            selections["HairStrands"] = strands
            selections["HairFixedWarmBangs"] = np.asarray([all(index in largest_hair_island for index in face.vertices)
                for face in faces]) & (hair_weight < .01) & warm & (saturation < .45) & (g > b + .015)
            selections["HairGoldOrnaments"] = np.asarray([all(index in ornaments for index in face.vertices)
                                                          for face in faces]) & gold
        for name, mask in selections.items():
            regions[name] = {"object": obj.name, "uv": uv[mask], "before": color[mask]}
    del source_pixels
    source_image.buffers_free()
    bpy.ops.wm.open_mainfile(filepath=str(final_path), load_ui=False, use_scripts=False)
    for object_name in EXPECTED_MATERIALS:
        material = bpy.data.objects[object_name].material_slots[0].material
        shader = next(node for node in material.node_tree.nodes if node.type == "BSDF_PRINCIPLED")
        for role, socket in (("after", "Base Color"), ("metallic", "Metallic")):
            image = upstream_images(shader.inputs[socket])[0]
            values, dimensions = pixels(image), tuple(image.size)
            for record in regions.values():
                if record["object"] == object_name:
                    record[role] = sample(values, dimensions, record["uv"])
            del values
            image.buffers_free()
    records, checks = {}, []
    for name, record in regions.items():
        before, after = record["before"], record["after"]
        if not len(before):
            records[name] = {"surface_samples": 0}
            continue
        difference = np.abs(after - before)
        metal = record["metallic"][:, 0]
        records[name] = {"surface_samples": len(before), "before_mean_rgb": before.mean(axis=0).tolist(),
            "after_mean_rgb": after.mean(axis=0).tolist(), "mean_abs_rgb_change": float(difference.mean()),
            "p95_abs_rgb_change": float(np.percentile(difference, 95)),
            "changed_fraction_over_002": float((difference.max(axis=1) > .02).mean()),
            "metallic_mean": float(metal.mean()), "metallic_p95": float(np.percentile(metal, 95)),
            "pink_chroma_shift": float(((after[:, 0] + after[:, 2]) / 2 - after[:, 1]
                                      - (before[:, 0] + before[:, 2]) / 2 + before[:, 1]).mean())}
        if name in target_colors:
            target = target_colors[name]
            before_chroma = before / np.maximum(before.sum(axis=1, keepdims=True), 1e-8)
            after_chroma = after / np.maximum(after.sum(axis=1, keepdims=True), 1e-8)
            target_chroma = target / target.sum()
            before_distance = float(np.abs(before_chroma - target_chroma).mean())
            after_distance = float(np.abs(after_chroma - target_chroma).mean())
            records[name].update({"reference_target_srgb": target.tolist(),
                "reference_chromaticity_distance_before": before_distance,
                "reference_chromaticity_distance_after": after_distance,
                "reference_chromaticity_distance_improvement": before_distance - after_distance,
                "red_minus_green_gain": float(((after[:, 0] - after[:, 1]) - (before[:, 0] - before[:, 1])).mean())})
    def check(name, passed, **details):
        checks.append({"name": name, "passed": bool(passed), **details})
    navy, tips = records["BodyNavy"], records["HairPinkTips"]
    check("Body navy recolor reaches real UV surface interiors", navy["surface_samples"] >= 20
          and navy.get("mean_abs_rgb_change", 0) > .003
          and navy.get("reference_chromaticity_distance_improvement", 0) > .001, region="BodyNavy")
    # The supplied reference is warm salmon pink: red/green contrast grows,
    # while blue need not grow. A magenta metric would reject the right hue.
    check("Hair tips approach the reference warm blush pink on weighted strand interiors", tips["surface_samples"] >= 20
          and tips.get("mean_abs_rgb_change", 0) > .006 and tips.get("red_minus_green_gain", 0) > .005
          and tips.get("reference_chromaticity_distance_improvement", 0) > .001, region="HairPinkTips")
    roots = records["HairWarmRoots"]
    check("Hair roots retain warm cream rather than silver", roots["surface_samples"] >= 20
          and roots["after_mean_rgb"][0] > roots["after_mean_rgb"][2] + .025, region="HairWarmRoots")
    for name in ("SkinFace", "SkinLeftHand", "SkinRightHand"):
        item = records[name]
        check(name + " preserves painted detail and stays nonmetallic", item["surface_samples"] >= 20
              and item.get("p95_abs_rgb_change", 1) < .06 and item.get("metallic_p95", 1) < .03, region=name)
    for name in ("HairStrands", "HairFixedWarmBangs"):
        item = records[name]
        check(name + " stays nonmetallic without gold foil", item["surface_samples"] >= 20
              and item.get("metallic_mean", 1) < .02 and item.get("metallic_p95", 1) < .04, region=name)
    for name in ("BodyGold", "HairGoldOrnaments"):
        item = records[name]
        check(name + " retains a metallic gold response", item["surface_samples"] >= 3
              and item.get("metallic_mean", 0) > .20, region=name)
    unchanged = file_sha(source_path) == source_hash and file_sha(final_path) == final_hash
    check("Semantic sampling preserves source and final file bytes", unchanged)
    report = {"source": source_path.name, "source_sha256": source_hash, "final": final_path.name,
              "final_sha256": final_hash, "sampling": "Bilinear samples at original mesh polygon UV centroids; atlas padding is excluded.",
              "checks": checks, "regions": records, "passed": all(item["passed"] for item in checks),
              "unity_import_or_play_verified": False}
    report_path.parent.mkdir(exist_ok=True, parents=True)
    report_path.write_text(json.dumps(report, indent=2, allow_nan=False) + "\n", encoding="utf-8", newline="\n")
    return report


def validate(source_path, final_path, report_path, manifest_path=None):
    source_path, final_path, report_path = map(lambda path: Path(path).resolve(),
                                               (source_path, final_path, report_path))
    source_hash, final_hash = file_sha(source_path), file_sha(final_path)
    manifest_path = Path(manifest_path).resolve() if manifest_path else None
    manifest = json.loads(manifest_path.read_text(encoding="utf-8")) if manifest_path else {}
    declarations = manifest.get("materials", {})
    bpy.ops.wm.open_mainfile(filepath=str(source_path), load_ui=False, use_scripts=False)
    before = capture()
    bpy.ops.wm.open_mainfile(filepath=str(final_path), load_ui=False, use_scripts=False)
    after = capture()
    checks = []
    def check(name, passed, **details):
        checks.append({"name": name, "passed": bool(passed), **details})
    check("All74 rest bones and hierarchy are exact", before["bones"] == after["bones"] and len(after["bones"]) == 74)
    check("Armature transform and evaluated pose bases are exact", before["armature_world"] == after["armature_world"]
          and before["pose_position"] == after["pose_position"] and before["pose_bases"] == after["pose_bases"])
    check("All mesh objects survive with exact geometry and every existing UV", before["meshes"].keys() == after["meshes"].keys()
          and all(before["meshes"][name]["geometry"] == after["meshes"][name]["geometry"] for name in before["meshes"]))
    check("Body15802 and Hair10447 topology counts remain", after["meshes"]["Head"]["vertices"] == 15802
          and after["meshes"]["Hair"]["vertices"] == 10447)
    skin_keys = ("weights", "groups", "parent", "matrix_world", "matrix_parent_inverse", "modifiers")
    check("Every skin weight group parent transform and modifier is exact", all(
          all(before["meshes"][name][key] == after["meshes"][name][key] for key in skin_keys) for name in before["meshes"]))
    check("Polygon material assignments are unchanged", all(before["meshes"][name]["polygon_material_indices"]
          == after["meshes"][name]["polygon_material_indices"] for name in before["meshes"]))
    expected_slots = {name: list(item["slots"]) for name, item in before["meshes"].items()}
    for name, material in EXPECTED_MATERIALS.items():
        expected_slots[name][0] = material
    check("Only Head and Hair slot0 use the declared new materials", all(expected_slots[name] == after["meshes"][name]["slots"]
          for name in expected_slots), slots={name: after["meshes"][name]["slots"] for name in EXPECTED_MATERIALS})
    check("All source Actions retain exact curves slots ranges and identities", before["actions"].keys() == after["actions"].keys()
          and all(before["actions"][name]["hash"] == after["actions"][name]["hash"]
                  and before["actions"][name]["slots"] == after["actions"][name]["slots"]
                  and before["actions"][name]["range"] == after["actions"][name]["range"] for name in before["actions"]),
          action_count=len(after["actions"]))
    check("Playback and Action/Slot/NLA binding are exact", before["animation"] == after["animation"] and before["playback"] == after["playback"])
    check("All source material graphs remain exact", all(after["materials"].get(name) == value for name, value in before["materials"].items()))
    check("Every source FILE image retains exact source bytes and interpretation", all(after["images"].get(name) == value
          for name, value in before["images"].items()), source_images=list(before["images"]))
    map_results, image_names = [], set()
    for object_name, material_name in EXPECTED_MATERIALS.items():
        material = bpy.data.materials.get(material_name)
        if not material or not material.node_tree:
            check(material_name + " has a shader graph", False)
            continue
        shaders = [node for node in material.node_tree.nodes if node.type == "BSDF_PRINCIPLED"]
        outputs = [node for node in material.node_tree.nodes if node.type == "OUTPUT_MATERIAL" and node.is_active_output]
        direct_shader = len(shaders) == 1 and len(outputs) == 1 and outputs[0].inputs["Surface"].is_linked and all(
                       link.from_node == shaders[0] for link in outputs[0].inputs["Surface"].links)
        check(material_name + " has one connected opaque Principled shader", direct_shader and not shaders[0].inputs["Alpha"].is_linked
              and shaders[0].inputs["Alpha"].default_value >= .999)
        if not shaders:
            continue
        shader = shaders[0]
        invalid_inputs = []
        for node in material.node_tree.nodes:
            for socket in node.inputs:
                if not hasattr(socket, "default_value"):
                    continue
                value = socket.default_value
                if isinstance(value, (int, float)):
                    values = [value]
                else:
                    try:
                        values = list(value)
                    except TypeError:
                        continue
                if any(isinstance(number, (int, float)) and not math.isfinite(number) for number in values):
                    invalid_inputs.append(node.name + "/" + socket.name)
        check(material_name + " shader inputs and texture mappings are finite", not invalid_inputs, invalid_inputs=invalid_inputs)
        declared_material = declarations.get(object_name, {})
        check(material_name + " matches its optional material declaration", not declared_material or declared_material.get("name") == material_name)
        for role, socket_name in ROLES.items():
            images = upstream_images(shader.inputs[socket_name])
            if len(images) != 1:
                check(material_name + "/" + role + " has exactly one texture input", False, images=[image.name for image in images])
                continue
            image = images[0]
            image_names.add(image.name)
            identity = image_identity(image)
            declared = declared_material.get("maps", {}).get(role, {})
            dimensions = [int(declared.get("width", 4096)), int(declared.get("height", 4096))]
            color_space = "sRGB" if role == "BaseColor" else "Non-Color"
            summary = pixel_summary(image)
            normal_nodes = [node for node in material.node_tree.nodes if node.type == "NORMAL_MAP"]
            passed = (identity["packed"] and identity["payload_sha256"] and identity["size"] == dimensions
                      and identity["colorspace"] == color_space and summary["finite"]
                      and summary["minimum"] >= -1e-6 and summary["maximum"] <= 1.000001)
            if role == "Normal":
                passed &= bool(normal_nodes and any(node.space == "TANGENT" and image in upstream_images(node.inputs["Color"])
                               for node in normal_nodes))
            if role in {"Roughness", "Metallic"}:
                passed &= summary.get("rgb_grayscale_max_error", 1) < 1e-5
            if declared:
                passed &= not declared.get("image") or declared["image"] == image.name
                passed &= not declared.get("sha256") or declared["sha256"] == identity["payload_sha256"]
                if declared.get("path"):
                    path = manifest_path.parent / declared["path"]
                    passed &= path.is_file() and file_sha(path) == identity["payload_sha256"]
            item = {"object": object_name, "role": role, "image": image.name, "identity": identity,
                    "pixels": summary, "passed": bool(passed)}
            map_results.append(item)
            check(material_name + "/" + role + " is packed finite and correctly interpreted", passed, image=image.name,
                  size=identity["size"], colorspace=identity["colorspace"], packed_sha256=identity["payload_sha256"])
    check("Body and Hair retain eight separate baked map images", len(map_results) == 8 and len(image_names) == 8)
    check("Source and final files remain byte-identical during validation", file_sha(source_path) == source_hash and file_sha(final_path) == final_hash)
    result = {"source": source_path.name, "source_sha256": source_hash, "final": final_path.name, "final_sha256": final_hash,
              "checks": checks, "maps": map_results, "passed": all(item["passed"] for item in checks),
              "unity_import_or_play_verified": False, "files_saved_by_validator": False}
    report_path.parent.mkdir(parents=True, exist_ok=True)
    report_path.write_text(json.dumps(result, indent=2, allow_nan=False) + "\n", encoding="utf-8", newline="\n")
    return result


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", required=True)
    parser.add_argument("--final", required=True)
    parser.add_argument("--report", required=True)
    parser.add_argument("--manifest")
    parser.add_argument("--color-report")
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:])
    result = validate(args.source, args.final, args.report, args.manifest)
    print("TEXTURED_CHARACTER_QA=" + json.dumps({"passed": result["passed"], "checks": len(result["checks"]),
          "failed": [item["name"] for item in result["checks"] if not item["passed"]]}))
    if args.color_report:
        colors = semantic_color_validation(args.source, args.final, args.color_report)
        print("CONCEPT_SURFACE_COLOR_QA=" + json.dumps({"passed": colors["passed"], "checks": len(colors["checks"]),
              "failed": [item["name"] for item in colors["checks"] if not item["passed"]]}))
        result["passed"] &= colors["passed"]
    if not result["passed"]:
        raise SystemExit(1)
