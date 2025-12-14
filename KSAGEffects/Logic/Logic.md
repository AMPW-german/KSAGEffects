# Goal:
Creating an independet logic system for simulating highly realistic g effects on humans

## Simulated effects:
- Blackout/Redout
- Push-Pull effect
- GLoC
- ALoC
- G induced Death

## Inputs:
- deltaTime: double - Time elapsed since the last update (in seconds)
- gForce vector: Vector3 - The current g-force vector applied to the human body (in g's)
- Human position?: maybe if the human/kitten sits or lies down because that changes the effect of g-forces on the body although it'll be hard to determine that without user input

## Outputs:
- consiousnessLevel: double (0.0 to 1.0, 2.0 for death)
- confusionLevel: double (0.0 to 1.0)
- TunnelVisionLevel: double (0.0 to 1.0)
- GreyScaleLevel: double (0.0 to 1.0)
- Color: bool (black vs red)

## Internal data:
WIP
