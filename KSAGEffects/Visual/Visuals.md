# Goals:

- Fully shader based visual effects with no dependencies on sprites or textures.
- Blackout/redout, loss of vision, tunnel vision, greyout, (blurriness?) effects
- changeable colors for blackout and redout effects
- Effect limits (e.g. no 100% blackout because not everyone wants maximum realism)
- Tunnel vision follows the mouse cursor (with limits to how far it can go off center) to simulate eye movement
- Not really part of visuals but related: confusion with chance based wrong inputs

# Current status:

Currently there's no known way of using custom shaders in KSA so for now the logic is the main focus.