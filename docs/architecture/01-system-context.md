# C4 level 1 — System context

```mermaid
flowchart LR
    Operator[Operator] --> HMI[Virtual Smart Motion Cell]
    Engineer[Automation engineer] --> HMI
    Recruiter[Reviewer / recruiter] --> Viewer[Browser digital twin]
    Contributor[Open-source contributor] --> SDK[Adapter SDK]
    HMI --> Cell[Machine software platform]
    Viewer --> Cell
    SDK --> Cell
    Cell --> Data[(Local production evidence)]
    Cell -. optional .-> MES[Manufacturing system]
    Cell -. optional .-> Controller[Motion / PLC controller]
```

The software platform owns machine sequencing, command validation, simulated motion, alarms, recovery, traceability, and state publication. External controllers and manufacturing systems are optional adapters.
