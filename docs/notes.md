## Ball spawner

- Visuele Cube child gebruikt om spawner-positie te zien
- Belangrijk: Box Collider op die Cube UITZETTEN
- Anders botst de spawnende bal direct met de Cube en vliegt random opzij
- Lesson: visuele helpers nooit met collider laten staan

## Observations

- Agent moet kunnen zien wat hij moet leren ontwijken
- Eerste setup had alleen eigen positie -> leerde niks
- Toegevoegd: bal positie + velocity + "bal bestaat" flag
- Vector Space Size in Inspector moet matchen met aantal AddObservation calls

## Eerste werkende training

- Mean Reward sprong van -0.87 naar -0.32 op step 10k
- Twee fixes samen:
  1. Relatieve bal-positie (bal_pos - agent_pos) ipv absolute
  2. Survival reward 0.001 -> 0.005

## Lesson

- Absolute observations dwingen het netwerk om "afstand"
  zelf te leren berekenen. Geef relatieve waardes als de
  policy "vlucht voor X" gedrag moet leren.
- Sterke -1 hit penalty domineert te veel als survival
  reward te klein is. Verhouding moet beter.

## dodge_v5 baseline

- Mean Reward eindigt op +0.477
- Maar: agent leert "altijd naar linkermuur rennen"
- Lokaal optimum, geen reactief gedrag
- Goed voorbeeld voor tutorial: cijfers ≠ goed gedrag
