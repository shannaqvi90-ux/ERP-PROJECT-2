# p09 — What the round adds to the shared gates

Date: 2026-11-06. Piece: p09-custom-fields, round 1. Status: accepted.

Endpoints added (each is attacked by G1 and G2): 5, all under `/api/customfields/`:
`GET /fields`, `GET /fields/{id}`, `POST /fields`, `PUT /fields/{id}`, `GET /record-types`; plus the
standard saved-view endpoints of one new list (`customfields.fields`, 9 more, generated). No value
endpoint exists: values travel with the contacts endpoints. Endpoints changed: the contact create,
update, get and defaults carry `customFields` (documented body and answer fields), and every list
definition endpoint adds the workspace's fields as columns.

List columns added to the gates' reach: 9 filterable and sortable columns of the definitions list
(in memory, 4 groupable). The custom fields themselves are columns only for a workspace that defines
them, and are not sortable or groupable; the gate workspaces define 4 fields on contacts (3 filterable
text/select/date/flag columns beyond the contact's own), so the contacts list gains 4 filter attacks
and its definition 4 columns.

Data added to the gate workspaces: 4 definitions and 2 to 4 values per contact (25 contacts a tenant;
more at the list-volume gates), each audited twice (the capture trigger and the record history).
The demo adds 4 definitions and about 270,000 values on the 100,000 contacts.

Processor time: measured by the verify run recorded in the builder's notes; a new piece's first
integration may raise the maximum by at most 3,500 s (owner decision #34).
