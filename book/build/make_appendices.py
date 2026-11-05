"""Generates Appendix E (troubleshooting) and Appendix I (links) from the
chapters' error tables and reference lists. Run from book/chapters."""
import re, glob

files = sorted(glob.glob('ch*.md'))
E = ['---', 'chapter: "E"', 'appendix: true',
     'title: "Troubleshooting Catalogue"', '---', '',
     'Every error table from the chapters, in one place, by chapter. Search '
     'this appendix for the message or symptom you see; the chapter has the '
     'context.', '']
links = {}
for f in files:
    s = open(f).read()
    num = int(re.search(r'^chapter:\s*(\d+)', s, re.M).group(1))
    title = re.search(r'^title:\s*"(.+)"', s, re.M).group(1)
    m = re.search(r':::\s*\{#tbl:errors[^}]*\}\n(.*?)\n:::', s, re.S)
    if m:
        rows = [l for l in m.group(1).splitlines() if l.startswith('|')]
        E += [f'## Chapter {num} – {title}', '',
              '| Symptom | Likely cause | Fix |', '|---|---|---|'] + rows[2:] + ['']
    refs = s.split('## References', 1)
    if len(refs) > 1:
        for l in refs[1].splitlines():
            mm = re.match(r'- \[(.+?)\]\((https?://[^)]+)\)', l)
            if mm and 'learn.microsoft.com' in mm.group(2):
                links.setdefault(mm.group(2), (mm.group(1), num))
open('appE-troubleshooting.md', 'w').write('\n'.join(E))

order = ['Azure Functions', 'Durable Functions and Durable Task Scheduler',
         'Dataverse and Power Platform',
         'Azure messaging, storage and API Management',
         'Identity and security', '.NET', 'Architecture and operations']
groups = {g: [] for g in order}
for url, (t, n) in sorted(links.items(), key=lambda x: x[1][0].lower()):
    u = url.lower()
    if '/durable-task/' in u: g = order[1]
    elif '/azure-functions/' in u: g = order[0]
    elif any(k in u for k in ('power-apps', 'power-platform', 'power-pages',
                              'dynamics365', '/connectors/')): g = order[2]
    elif any(k in u for k in ('service-bus', '/storage/', 'api-management',
                              'load-testing')): g = order[3]
    elif any(k in u for k in ('/entra/', 'app-service', '/github/')): g = order[4]
    elif '/dotnet/' in u: g = order[5]
    else: g = order[6]
    groups[g].append(f'- [{t}]({url}) (Chapter {n})')
I = ['---', 'chapter: "I"', 'appendix: true',
     'title: "Official Microsoft Documentation"', '---', '',
     "The Microsoft Learn pages this book relies on, grouped by area, with the "
     "chapter that first cites each. All were checked in October 2026; "
     "Microsoft updates them often, so read the current version before "
     "relying on a limit or a version number.", '']
for g in order:
    if groups[g]:
        I += [f'## {g}', ''] + groups[g] + ['']
open('appI-links.md', 'w').write('\n'.join(I))
print(len(links), 'links')
