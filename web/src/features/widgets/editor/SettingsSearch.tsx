import { PropertyField, type EditorKit } from './controls';
import { inMode, type DraftPath } from './draft';
import type { PropertyDef, Registry } from './registry';

function placesOf(registry: Registry): Map<DraftPath, string> {
  const places = new Map<DraftPath, string>();
  for (const section of registry.sections) {
    for (const id of section.properties) {
      places.set(id, section.label);
    }
  }
  for (const target of [...registry.targets].reverse()) {
    for (const id of target.properties) {
      places.set(id, target.label);
    }
  }
  return places;
}

export function SearchResults({ registry, kit, query }: { registry: Registry; kit: EditorKit; query: string }) {
  const places = placesOf(registry);
  const words = query.toLowerCase().split(/\s+/).filter(Boolean);
  const matches = [...registry.properties.values()].filter((property) => {
    if (!inMode(property.id, kit.mode)) {
      return false;
    }
    const haystack = `${property.label} ${property.hint ?? ''} ${places.get(property.id) ?? ''}`.toLowerCase();
    return words.every((word) => haystack.includes(word));
  });
  const groups = new Map<string, PropertyDef[]>();
  for (const property of matches) {
    const place = places.get(property.id) ?? 'Settings';
    groups.set(place, [...(groups.get(place) ?? []), property]);
  }
  return (
    <div className="space-y-5 p-4" role="region" aria-label="Search results">
      <p role="status" className="text-xs text-fg-muted">
        {matches.length === 0 ? 'Nothing matches.' : matches.length === 1 ? '1 setting' : `${matches.length} settings`}
      </p>
      {[...groups].map(([place, properties]) => (
        <div key={place} className="space-y-3">
          <h2 className="text-xs font-medium text-fg-muted">{place}</h2>
          {properties.map((property) => (
            <PropertyField key={property.id} property={property} kit={kit} />
          ))}
        </div>
      ))}
    </div>
  );
}
