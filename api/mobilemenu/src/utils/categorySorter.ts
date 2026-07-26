/**
 * Sorts category names based on the custom rules:
 * 1. Dinner comes immediately after Breakfast
 * 2. Tea and Coffee are kept together (adjacent)
 * 3. Cold drinks (matching cold, drink, pepsi, cola) are moved to the very end
 */
export function sortCategories(categories: string[]): string[] {
  const breakfastCats: string[] = [];
  const dinnerCats: string[] = [];
  const teaCats: string[] = [];
  const coffeeCats: string[] = [];
  const coldDrinkCats: string[] = [];
  const otherCats: string[] = [];

  for (const cat of categories) {
    if (!cat) continue;
    const lower = cat.toLowerCase();
    if (lower.includes('breakfast')) {
      breakfastCats.push(cat);
    } else if (lower.includes('dinner')) {
      dinnerCats.push(cat);
    } else if (lower.includes('tea')) {
      teaCats.push(cat);
    } else if (lower.includes('coffee')) {
      coffeeCats.push(cat);
    } else if (
      lower.includes('cold') || 
      lower.includes('drink') || 
      lower.includes('pepsi') || 
      lower.includes('cola') || 
      lower.includes('coldink')
    ) {
      coldDrinkCats.push(cat);
    } else {
      otherCats.push(cat);
    }
  }

  return [
    ...breakfastCats,
    ...dinnerCats,
    ...teaCats,
    ...coffeeCats,
    ...otherCats,
    ...coldDrinkCats
  ];
}
