---
name: unity-sample
description: "Sample/test skills. Use for basic API testing. Triggers: test, sample, hello, ping."
---

# Sample Skills

Basic examples for testing the API.

## Skills

### create_cube
Create a cube primitive.
**Parameters:** `x`, `y`, `z`, `name`

### create_sphere
Create a sphere primitive.
**Parameters:** `x`, `y`, `z`, `name`

### delete_object
Delete object by name.
**Parameters:** `objectName`

### `find_objects_by_name`
Find objects containing string.
**Parameters:** `nameContains`

### `set_object_position`
Set object position.
**Parameters:** `objectName`, `x`, `y`, `z`

### `set_object_rotation`
Set object rotation.
**Parameters:** `objectName`, `x`, `y`, `z`

### `set_object_scale`
Set object scale.
**Parameters:** `objectName`, `x`, `y`, `z`

### `get_scene_info`
Get current scene information.
**Parameters:** None.
