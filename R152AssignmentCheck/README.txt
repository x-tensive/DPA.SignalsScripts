DPA-6249.
Script sends web-notification to Technology group users about lack of production output accounting
if the downloaded CP file doesn't contain reference to R152, or doesn't modify its value.

1. Create two message templates from RegisterWriteCheckHandler.cs
2. For each register to be checked, create workcenter group with name as described in RegisterWriteCheckHandler.cs, like "CHECK WRITE OPERATION R15", "CHECK WRITE OPERATION R27", etc.
3. Assign workcenter, on which certain register(s) must be checked in control programs, to workcenter group(s) with corresponding name(s)