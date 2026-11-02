using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CDTag.Common.Json;
using CDTag.Common.Tests.Json.Model;
using NUnit.Framework;
using NUnit.Framework.Legacy;

namespace CDTag.Common.Tests.Json
{
    [TestFixture]
    public class JsonSerializerTest
    {
        [Test]
        public void DeserializeColorValue_SimpleClass()
        {
            // Act
            ColorValue colorValue = JsonSerializer.ReadObject<ColorValue>(Properties.Resources.colorvalue_json);

            // Assert
            AssertColorValue(colorValue);
        }

        [Test]
        public void SerializeColorValue_SimpleClass()
        {
            // Arrange
            ColorValue colorValue = JsonSerializer.ReadObject<ColorValue>(Properties.Resources.colorvalue_json);

            // Act
            string json = JsonSerializer.SerializeObject(colorValue);
            ColorValue newColorValue = JsonSerializer.ReadObject<ColorValue>(json);

            // Assert
            AssertColorValue(newColorValue);
        }

        private static void AssertColorValue(ColorValue colorValue)
        {
            ClassicAssert.IsNotNull(colorValue);
            ClassicAssert.AreEqual("red", colorValue.Color);
            ClassicAssert.AreEqual("#f00", colorValue.Value);
        }

        [Test]
        public void DeserializeRecipe_NestedClassesAndArrays()
        {
            // Act
            Recipe recipe = JsonSerializer.ReadObject<Recipe>(Properties.Resources.recipe_json);

            // Assert
            AssertRecipe(recipe);
        }

        [Test]
        public void SerializeRecipe_NestedClassesAndArrays()
        {
            // Arrange
            Recipe recipe = JsonSerializer.ReadObject<Recipe>(Properties.Resources.recipe_json);

            // Act
            string json = JsonSerializer.SerializeObject(recipe);
            Recipe newRecipe = JsonSerializer.ReadObject<Recipe>(json);

            // Assert
            AssertRecipe(newRecipe);
        }

        private static void AssertRecipe(Recipe recipe)
        {
            ClassicAssert.IsNotNull(recipe);
            ClassicAssert.IsNotNull(recipe.Batters);
            ClassicAssert.IsNotNull(recipe.Batters.Batter);
            ClassicAssert.IsNotNull(recipe.Topping);

            ClassicAssert.IsNull(recipe.Fillings);
            ClassicAssert.IsNull(recipe.Image);
            ClassicAssert.IsNull(recipe.Thumbnail);

            ClassicAssert.AreEqual("0001", recipe.ID);
            ClassicAssert.AreEqual("donut", recipe.Type);
            ClassicAssert.AreEqual("Cake", recipe.Name);
            ClassicAssert.AreEqual(0.55, recipe.PPU);

            ClassicAssert.AreEqual(4, recipe.Batters.Batter.Length);
            ClassicAssert.AreEqual("1001", recipe.Batters.Batter[0].ID);
            ClassicAssert.AreEqual("Regular", recipe.Batters.Batter[0].Type);
            ClassicAssert.AreEqual("1002", recipe.Batters.Batter[1].ID);
            ClassicAssert.AreEqual("Chocolate", recipe.Batters.Batter[1].Type);
            ClassicAssert.AreEqual("1003", recipe.Batters.Batter[2].ID);
            ClassicAssert.AreEqual("Blueberry", recipe.Batters.Batter[2].Type);
            ClassicAssert.AreEqual("1004", recipe.Batters.Batter[3].ID);
            ClassicAssert.AreEqual("Devil's Food", recipe.Batters.Batter[3].Type);

            ClassicAssert.AreEqual(7, recipe.Topping.Length);
            ClassicAssert.AreEqual("5001", recipe.Topping[0].ID);
            ClassicAssert.AreEqual("None", recipe.Topping[0].Type);
            ClassicAssert.AreEqual("5002", recipe.Topping[1].ID);
            ClassicAssert.AreEqual("Glazed", recipe.Topping[1].Type);
            ClassicAssert.AreEqual("5005", recipe.Topping[2].ID);
            ClassicAssert.AreEqual("Sugar", recipe.Topping[2].Type);
            ClassicAssert.AreEqual("5007", recipe.Topping[3].ID);
            ClassicAssert.AreEqual("Powdered Sugar", recipe.Topping[3].Type);
            ClassicAssert.AreEqual("5006", recipe.Topping[4].ID);
            ClassicAssert.AreEqual("Chocolate with Sprinkles", recipe.Topping[4].Type);
            ClassicAssert.AreEqual("5003", recipe.Topping[5].ID);
            ClassicAssert.AreEqual("Chocolate", recipe.Topping[5].Type);
            ClassicAssert.AreEqual("5004", recipe.Topping[6].ID);
            ClassicAssert.AreEqual("Maple", recipe.Topping[6].Type);
        }

        [Test]
        public void DeserializeRecipeArray_ArrayOfClasses()
        {
            // Act
            Recipe[] recipes = JsonSerializer.ReadObject<Recipe[]>(Properties.Resources.recipe_array_json);

            // Assert
            AssertRecipeArray(recipes);
        }

        [Test]
        public void SerializeRecipeArray_ArrayOfClasses()
        {
            // Arrange
            Recipe[] recipes = JsonSerializer.ReadObject<Recipe[]>(Properties.Resources.recipe_array_json);

            // Act
            string json = JsonSerializer.SerializeObject(recipes);
            Recipe[] newRecipes = JsonSerializer.ReadObject<Recipe[]>(json);

            // Assert
            AssertRecipeArray(newRecipes);
        }

        private static void AssertRecipeArray(Recipe[] recipes)
        {
            // Assert
            ClassicAssert.IsNotNull(recipes);
            ClassicAssert.AreEqual(3, recipes.Length);

            // Assert recipes[0]
            {
                Recipe recipe = recipes[0];

                ClassicAssert.IsNotNull(recipe);
                ClassicAssert.IsNotNull(recipe.Batters);
                ClassicAssert.IsNotNull(recipe.Batters.Batter);
                ClassicAssert.IsNotNull(recipe.Topping);

                ClassicAssert.IsNull(recipe.Fillings);
                ClassicAssert.IsNull(recipe.Image);
                ClassicAssert.IsNull(recipe.Thumbnail);

                ClassicAssert.AreEqual("0001", recipe.ID);
                ClassicAssert.AreEqual("donut", recipe.Type);
                ClassicAssert.AreEqual("Cake", recipe.Name);
                ClassicAssert.AreEqual(0.55, recipe.PPU);

                ClassicAssert.AreEqual(4, recipe.Batters.Batter.Length);
                ClassicAssert.AreEqual("1001", recipe.Batters.Batter[0].ID);
                ClassicAssert.AreEqual("Regular", recipe.Batters.Batter[0].Type);
                ClassicAssert.AreEqual("1002", recipe.Batters.Batter[1].ID);
                ClassicAssert.AreEqual("Chocolate", recipe.Batters.Batter[1].Type);
                ClassicAssert.AreEqual("1003", recipe.Batters.Batter[2].ID);
                ClassicAssert.AreEqual("Blueberry", recipe.Batters.Batter[2].Type);
                ClassicAssert.AreEqual("1004", recipe.Batters.Batter[3].ID);
                ClassicAssert.AreEqual("Devil's Food", recipe.Batters.Batter[3].Type);

                ClassicAssert.AreEqual(7, recipe.Topping.Length);
                ClassicAssert.AreEqual("5001", recipe.Topping[0].ID);
                ClassicAssert.AreEqual("None", recipe.Topping[0].Type);
                ClassicAssert.AreEqual("5002", recipe.Topping[1].ID);
                ClassicAssert.AreEqual("Glazed", recipe.Topping[1].Type);
                ClassicAssert.AreEqual("5005", recipe.Topping[2].ID);
                ClassicAssert.AreEqual("Sugar", recipe.Topping[2].Type);
                ClassicAssert.AreEqual("5007", recipe.Topping[3].ID);
                ClassicAssert.AreEqual("Powdered Sugar", recipe.Topping[3].Type);
                ClassicAssert.AreEqual("5006", recipe.Topping[4].ID);
                ClassicAssert.AreEqual("Chocolate with Sprinkles", recipe.Topping[4].Type);
                ClassicAssert.AreEqual("5003", recipe.Topping[5].ID);
                ClassicAssert.AreEqual("Chocolate", recipe.Topping[5].Type);
                ClassicAssert.AreEqual("5004", recipe.Topping[6].ID);
                ClassicAssert.AreEqual("Maple", recipe.Topping[6].Type);
            }

            // Assert recipes[1]
            {
                Recipe recipe = recipes[1];

                ClassicAssert.IsNotNull(recipe);
                ClassicAssert.IsNotNull(recipe.Batters);
                ClassicAssert.IsNotNull(recipe.Batters.Batter);
                ClassicAssert.IsNotNull(recipe.Topping);

                ClassicAssert.IsNull(recipe.Fillings);
                ClassicAssert.IsNull(recipe.Image);
                ClassicAssert.IsNull(recipe.Thumbnail);

                ClassicAssert.AreEqual("0002", recipe.ID);
                ClassicAssert.AreEqual("donut", recipe.Type);
                ClassicAssert.AreEqual("Raised", recipe.Name);
                ClassicAssert.AreEqual(0.55, recipe.PPU);

                ClassicAssert.AreEqual(1, recipe.Batters.Batter.Length);
                ClassicAssert.AreEqual("1001", recipe.Batters.Batter[0].ID);
                ClassicAssert.AreEqual("Regular", recipe.Batters.Batter[0].Type);

                ClassicAssert.AreEqual(5, recipe.Topping.Length);
                ClassicAssert.AreEqual("5001", recipe.Topping[0].ID);
                ClassicAssert.AreEqual("None", recipe.Topping[0].Type);
                ClassicAssert.AreEqual("5002", recipe.Topping[1].ID);
                ClassicAssert.AreEqual("Glazed", recipe.Topping[1].Type);
                ClassicAssert.AreEqual("5005", recipe.Topping[2].ID);
                ClassicAssert.AreEqual("Sugar", recipe.Topping[2].Type);
                ClassicAssert.AreEqual("5003", recipe.Topping[3].ID);
                ClassicAssert.AreEqual("Chocolate", recipe.Topping[3].Type);
                ClassicAssert.AreEqual("5004", recipe.Topping[4].ID);
                ClassicAssert.AreEqual("Maple", recipe.Topping[4].Type);
            }

            // Assert recipes[2]
            {
                Recipe recipe = recipes[2];

                ClassicAssert.IsNotNull(recipe);
                ClassicAssert.IsNotNull(recipe.Batters);
                ClassicAssert.IsNotNull(recipe.Batters.Batter);
                ClassicAssert.IsNotNull(recipe.Topping);

                ClassicAssert.IsNull(recipe.Fillings);
                ClassicAssert.IsNull(recipe.Image);
                ClassicAssert.IsNull(recipe.Thumbnail);

                ClassicAssert.AreEqual("0003", recipe.ID);
                ClassicAssert.AreEqual("donut", recipe.Type);
                ClassicAssert.AreEqual("Old Fashioned", recipe.Name);
                ClassicAssert.AreEqual(0.55, recipe.PPU);

                ClassicAssert.AreEqual(2, recipe.Batters.Batter.Length);
                ClassicAssert.AreEqual("1001", recipe.Batters.Batter[0].ID);
                ClassicAssert.AreEqual("Regular", recipe.Batters.Batter[0].Type);
                ClassicAssert.AreEqual("1002", recipe.Batters.Batter[1].ID);
                ClassicAssert.AreEqual("Chocolate", recipe.Batters.Batter[1].Type);

                ClassicAssert.AreEqual(4, recipe.Topping.Length);
                ClassicAssert.AreEqual("5001", recipe.Topping[0].ID);
                ClassicAssert.AreEqual("None", recipe.Topping[0].Type);
                ClassicAssert.AreEqual("5002", recipe.Topping[1].ID);
                ClassicAssert.AreEqual("Glazed", recipe.Topping[1].Type);
                ClassicAssert.AreEqual("5003", recipe.Topping[2].ID);
                ClassicAssert.AreEqual("Chocolate", recipe.Topping[2].Type);
                ClassicAssert.AreEqual("5004", recipe.Topping[3].ID);
                ClassicAssert.AreEqual("Maple", recipe.Topping[3].Type);
            }
        }

        [Test]
        public void DeserializeRecipe_NestedClasses()
        {
            // Act
            Recipe recipe = JsonSerializer.ReadObject<Recipe>(Properties.Resources.image_thumbnail_json);

            // Assert
            AssertRecipeWithImages(recipe);
        }

        [Test]
        public void SerializeRecipe_NestedClasses()
        {
            // Arrange
            Recipe recipe = JsonSerializer.ReadObject<Recipe>(Properties.Resources.image_thumbnail_json);

            // Act
            string json = JsonSerializer.SerializeObject(recipe);
            Recipe newRecipe = JsonSerializer.ReadObject<Recipe>(json);

            // Assert
            AssertRecipeWithImages(newRecipe);
        }

        private static void AssertRecipeWithImages(Recipe recipe)
        {
            // Assert
            ClassicAssert.IsNotNull(recipe);
            ClassicAssert.IsNotNull(recipe.Image);
            ClassicAssert.IsNotNull(recipe.Thumbnail);

            ClassicAssert.AreEqual("0001", recipe.ID);
            ClassicAssert.AreEqual("donut", recipe.Type);
            ClassicAssert.AreEqual("Cake", recipe.Name);

            ClassicAssert.AreEqual("images/0001.jpg", recipe.Image.Url);
            ClassicAssert.AreEqual(200, recipe.Image.Width);
            ClassicAssert.AreEqual(200, recipe.Image.Height);

            ClassicAssert.AreEqual("images/thumbnails/0001.jpg", recipe.Thumbnail.Url);
            ClassicAssert.AreEqual(32, recipe.Thumbnail.Width);
            ClassicAssert.AreEqual(32, recipe.Thumbnail.Height);
        }

        [Test]
        public void DeserializeDonuts_LargeNested()
        {
            // Act
            RecipeBook book = JsonSerializer.ReadObject<RecipeBook>(Properties.Resources.donuts_json);

            // Assert
            AssertDonuts(book);
        }

        [Test]
        public void SerializeDonuts_LargeNested()
        {
            // Arrange
            RecipeBook book = JsonSerializer.ReadObject<RecipeBook>(Properties.Resources.donuts_json);

            // Act
            string json = JsonSerializer.SerializeObject(book);
            RecipeBook newBook = JsonSerializer.ReadObject<RecipeBook>(json);

            // Assert
            AssertDonuts(newBook);
        }

        private static void AssertDonuts(RecipeBook book)
        {
            // Assert
            ClassicAssert.IsNotNull(book);
            ClassicAssert.IsNotNull(book.Items);
            ClassicAssert.IsNotNull(book.Items.Item);

            ClassicAssert.AreEqual(6, book.Items.Item.Length, "book.Items.Item.Length");

            // Assert book.Items.Item[0]
            {
                Recipe recipe = book.Items.Item[0];

                ClassicAssert.IsNotNull(recipe);
                ClassicAssert.IsNotNull(recipe.Batters);
                ClassicAssert.IsNotNull(recipe.Batters.Batter);
                ClassicAssert.IsNotNull(recipe.Topping);

                ClassicAssert.IsNull(recipe.Fillings);
                ClassicAssert.IsNull(recipe.Image);
                ClassicAssert.IsNull(recipe.Thumbnail);

                ClassicAssert.AreEqual("0001", recipe.ID);
                ClassicAssert.AreEqual("donut", recipe.Type);
                ClassicAssert.AreEqual("Cake", recipe.Name);
                ClassicAssert.AreEqual(0.55, recipe.PPU);

                ClassicAssert.AreEqual(4, recipe.Batters.Batter.Length);
                ClassicAssert.AreEqual("1001", recipe.Batters.Batter[0].ID);
                ClassicAssert.AreEqual("Regular", recipe.Batters.Batter[0].Type);
                ClassicAssert.AreEqual("1002", recipe.Batters.Batter[1].ID);
                ClassicAssert.AreEqual("Chocolate", recipe.Batters.Batter[1].Type);
                ClassicAssert.AreEqual("1003", recipe.Batters.Batter[2].ID);
                ClassicAssert.AreEqual("Blueberry", recipe.Batters.Batter[2].Type);
                ClassicAssert.AreEqual("1004", recipe.Batters.Batter[3].ID);
                ClassicAssert.AreEqual("Devil's Food", recipe.Batters.Batter[3].Type);

                ClassicAssert.AreEqual(7, recipe.Topping.Length);
                ClassicAssert.AreEqual("5001", recipe.Topping[0].ID);
                ClassicAssert.AreEqual("None", recipe.Topping[0].Type);
                ClassicAssert.AreEqual("5002", recipe.Topping[1].ID);
                ClassicAssert.AreEqual("Glazed", recipe.Topping[1].Type);
                ClassicAssert.AreEqual("5005", recipe.Topping[2].ID);
                ClassicAssert.AreEqual("Sugar", recipe.Topping[2].Type);
                ClassicAssert.AreEqual("5007", recipe.Topping[3].ID);
                ClassicAssert.AreEqual("Powdered Sugar", recipe.Topping[3].Type);
                ClassicAssert.AreEqual("5006", recipe.Topping[4].ID);
                ClassicAssert.AreEqual("Chocolate with Sprinkles", recipe.Topping[4].Type);
                ClassicAssert.AreEqual("5003", recipe.Topping[5].ID);
                ClassicAssert.AreEqual("Chocolate", recipe.Topping[5].Type);
                ClassicAssert.AreEqual("5004", recipe.Topping[6].ID);
                ClassicAssert.AreEqual("Maple", recipe.Topping[6].Type);
            }

            // Assert book.Items.Item[1]
            {
                Recipe recipe = book.Items.Item[1];

                ClassicAssert.IsNotNull(recipe);
                ClassicAssert.IsNotNull(recipe.Batters);
                ClassicAssert.IsNotNull(recipe.Batters.Batter);
                ClassicAssert.IsNotNull(recipe.Topping);

                ClassicAssert.IsNull(recipe.Fillings);
                ClassicAssert.IsNull(recipe.Image);
                ClassicAssert.IsNull(recipe.Thumbnail);

                ClassicAssert.AreEqual("0002", recipe.ID);
                ClassicAssert.AreEqual("donut", recipe.Type);
                ClassicAssert.AreEqual("Raised", recipe.Name);
                ClassicAssert.AreEqual(0.55, recipe.PPU);

                ClassicAssert.AreEqual(1, recipe.Batters.Batter.Length);
                ClassicAssert.AreEqual("1001", recipe.Batters.Batter[0].ID);
                ClassicAssert.AreEqual("Regular", recipe.Batters.Batter[0].Type);

                ClassicAssert.AreEqual(5, recipe.Topping.Length);
                ClassicAssert.AreEqual("5001", recipe.Topping[0].ID);
                ClassicAssert.AreEqual("None", recipe.Topping[0].Type);
                ClassicAssert.AreEqual("5002", recipe.Topping[1].ID);
                ClassicAssert.AreEqual("Glazed", recipe.Topping[1].Type);
                ClassicAssert.AreEqual("5005", recipe.Topping[2].ID);
                ClassicAssert.AreEqual("Sugar", recipe.Topping[2].Type);
                ClassicAssert.AreEqual("5003", recipe.Topping[3].ID);
                ClassicAssert.AreEqual("Chocolate", recipe.Topping[3].Type);
                ClassicAssert.AreEqual("5004", recipe.Topping[4].ID);
                ClassicAssert.AreEqual("Maple", recipe.Topping[4].Type);
            }

            // Assert book.Items.Item[2]
            {
                Recipe recipe = book.Items.Item[2];

                ClassicAssert.IsNotNull(recipe);
                ClassicAssert.IsNotNull(recipe.Batters);
                ClassicAssert.IsNotNull(recipe.Batters.Batter);
                ClassicAssert.IsNotNull(recipe.Topping);

                ClassicAssert.IsNull(recipe.Fillings);
                ClassicAssert.IsNull(recipe.Image);
                ClassicAssert.IsNull(recipe.Thumbnail);

                ClassicAssert.AreEqual("0003", recipe.ID);
                ClassicAssert.AreEqual("donut", recipe.Type);
                ClassicAssert.AreEqual("Old Fashioned", recipe.Name);
                ClassicAssert.AreEqual(0.55, recipe.PPU);

                ClassicAssert.AreEqual(2, recipe.Batters.Batter.Length);
                ClassicAssert.AreEqual("1001", recipe.Batters.Batter[0].ID);
                ClassicAssert.AreEqual("Regular", recipe.Batters.Batter[0].Type);
                ClassicAssert.AreEqual("1002", recipe.Batters.Batter[1].ID);
                ClassicAssert.AreEqual("Chocolate", recipe.Batters.Batter[1].Type);

                ClassicAssert.AreEqual(4, recipe.Topping.Length);
                ClassicAssert.AreEqual("5001", recipe.Topping[0].ID);
                ClassicAssert.AreEqual("None", recipe.Topping[0].Type);
                ClassicAssert.AreEqual("5002", recipe.Topping[1].ID);
                ClassicAssert.AreEqual("Glazed", recipe.Topping[1].Type);
                ClassicAssert.AreEqual("5003", recipe.Topping[2].ID);
                ClassicAssert.AreEqual("Chocolate", recipe.Topping[2].Type);
                ClassicAssert.AreEqual("5004", recipe.Topping[3].ID);
                ClassicAssert.AreEqual("Maple", recipe.Topping[3].Type);
            }

            // Assert book.Items.Item[3]
            {
                Recipe recipe = book.Items.Item[3];

                ClassicAssert.IsNotNull(recipe);
                ClassicAssert.IsNotNull(recipe.Batters);
                ClassicAssert.IsNotNull(recipe.Batters.Batter);
                ClassicAssert.IsNotNull(recipe.Fillings);
                ClassicAssert.IsNotNull(recipe.Fillings.Filling);

                ClassicAssert.IsNull(recipe.Image);
                ClassicAssert.IsNull(recipe.Thumbnail);

                ClassicAssert.AreEqual("0004", recipe.ID);
                ClassicAssert.AreEqual("bar", recipe.Type);
                ClassicAssert.AreEqual("Bar", recipe.Name);
                ClassicAssert.AreEqual(0.75, recipe.PPU);

                ClassicAssert.AreEqual(1, recipe.Batters.Batter.Length);
                ClassicAssert.AreEqual("1001", recipe.Batters.Batter[0].ID);
                ClassicAssert.AreEqual("Regular", recipe.Batters.Batter[0].Type);

                ClassicAssert.AreEqual(2, recipe.Topping.Length);
                ClassicAssert.AreEqual("5003", recipe.Topping[0].ID);
                ClassicAssert.AreEqual("Chocolate", recipe.Topping[0].Type);
                ClassicAssert.AreEqual("5004", recipe.Topping[1].ID);
                ClassicAssert.AreEqual("Maple", recipe.Topping[1].Type);

                ClassicAssert.AreEqual(3, recipe.Fillings.Filling.Length);
                ClassicAssert.AreEqual("7001", recipe.Fillings.Filling[0].ID);
                ClassicAssert.AreEqual("None", recipe.Fillings.Filling[0].Name);
                ClassicAssert.AreEqual(0, recipe.Fillings.Filling[0].AddCost);
                ClassicAssert.AreEqual("7002", recipe.Fillings.Filling[1].ID);
                ClassicAssert.AreEqual("Custard", recipe.Fillings.Filling[1].Name);
                ClassicAssert.AreEqual(0.25, recipe.Fillings.Filling[1].AddCost);
                ClassicAssert.AreEqual("7003", recipe.Fillings.Filling[2].ID);
                ClassicAssert.AreEqual("Whipped Cream", recipe.Fillings.Filling[2].Name);
                ClassicAssert.AreEqual(0.25, recipe.Fillings.Filling[2].AddCost);
            }

            // Assert book.Items.Item[4]
            {
                Recipe recipe = book.Items.Item[4];

                ClassicAssert.IsNotNull(recipe);
                ClassicAssert.IsNotNull(recipe.Batters);
                ClassicAssert.IsNotNull(recipe.Batters.Batter);
                ClassicAssert.IsNotNull(recipe.Topping);

                ClassicAssert.IsNull(recipe.Fillings);
                ClassicAssert.IsNull(recipe.Image);
                ClassicAssert.IsNull(recipe.Thumbnail);

                ClassicAssert.AreEqual("0005", recipe.ID);
                ClassicAssert.AreEqual("twist", recipe.Type);
                ClassicAssert.AreEqual("Twist", recipe.Name);
                ClassicAssert.AreEqual(0.65, recipe.PPU);

                ClassicAssert.AreEqual(1, recipe.Batters.Batter.Length);
                ClassicAssert.AreEqual("1001", recipe.Batters.Batter[0].ID);
                ClassicAssert.AreEqual("Regular", recipe.Batters.Batter[0].Type);

                ClassicAssert.AreEqual(2, recipe.Topping.Length);
                ClassicAssert.AreEqual("5002", recipe.Topping[0].ID);
                ClassicAssert.AreEqual("Glazed", recipe.Topping[0].Type);
                ClassicAssert.AreEqual("5005", recipe.Topping[1].ID);
                ClassicAssert.AreEqual("Sugar", recipe.Topping[1].Type);
            }

            // Assert book.Items.Item[5]
            {
                Recipe recipe = book.Items.Item[5];

                ClassicAssert.IsNotNull(recipe);
                ClassicAssert.IsNotNull(recipe.Batters);
                ClassicAssert.IsNotNull(recipe.Batters.Batter);
                ClassicAssert.IsNotNull(recipe.Topping);
                ClassicAssert.IsNotNull(recipe.Fillings);
                ClassicAssert.IsNotNull(recipe.Fillings.Filling);

                ClassicAssert.IsNull(recipe.Image);
                ClassicAssert.IsNull(recipe.Thumbnail);

                ClassicAssert.AreEqual("0006", recipe.ID);
                ClassicAssert.AreEqual("filled", recipe.Type);
                ClassicAssert.AreEqual("Filled", recipe.Name);
                ClassicAssert.AreEqual(0.75, recipe.PPU);

                ClassicAssert.AreEqual(1, recipe.Batters.Batter.Length);
                ClassicAssert.AreEqual("1001", recipe.Batters.Batter[0].ID);
                ClassicAssert.AreEqual("Regular", recipe.Batters.Batter[0].Type);

                ClassicAssert.AreEqual(4, recipe.Topping.Length);
                ClassicAssert.AreEqual("5002", recipe.Topping[0].ID);
                ClassicAssert.AreEqual("Glazed", recipe.Topping[0].Type);
                ClassicAssert.AreEqual("5007", recipe.Topping[1].ID);
                ClassicAssert.AreEqual("Powdered Sugar", recipe.Topping[1].Type);
                ClassicAssert.AreEqual("5003", recipe.Topping[2].ID);
                ClassicAssert.AreEqual("Chocolate", recipe.Topping[2].Type);
                ClassicAssert.AreEqual("5004", recipe.Topping[3].ID);
                ClassicAssert.AreEqual("Maple", recipe.Topping[3].Type);

                ClassicAssert.AreEqual(4, recipe.Fillings.Filling.Length);
                ClassicAssert.AreEqual("7002", recipe.Fillings.Filling[0].ID);
                ClassicAssert.AreEqual("Custard", recipe.Fillings.Filling[0].Name);
                ClassicAssert.AreEqual(0, recipe.Fillings.Filling[0].AddCost);
                ClassicAssert.AreEqual("7003", recipe.Fillings.Filling[1].ID);
                ClassicAssert.AreEqual("Whipped Cream", recipe.Fillings.Filling[1].Name);
                ClassicAssert.AreEqual(0, recipe.Fillings.Filling[1].AddCost);
                ClassicAssert.AreEqual("7004", recipe.Fillings.Filling[2].ID);
                ClassicAssert.AreEqual("Strawberry Jelly", recipe.Fillings.Filling[2].Name);
                ClassicAssert.AreEqual(0, recipe.Fillings.Filling[2].AddCost);
                ClassicAssert.AreEqual("7005", recipe.Fillings.Filling[3].ID);
                ClassicAssert.AreEqual("Rasberry Jelly", recipe.Fillings.Filling[3].Name);
                ClassicAssert.AreEqual(0, recipe.Fillings.Filling[3].AddCost);
            }
        }

        [Test]
        public void DeserializeIntArray()
        {
            // Act
            int[] intArray = JsonSerializer.ReadObject<int[]>(Properties.Resources.int_array_json);

            // Assert
            AssertIntArray(intArray);
        }

        [Test]
        public void SerializeIntArray()
        {
            // Arrange
            int[] intArray = JsonSerializer.ReadObject<int[]>(Properties.Resources.int_array_json);

            // Act
            string json = JsonSerializer.SerializeObject(intArray);
            int[] newIntArray = JsonSerializer.ReadObject<int[]>(json);

            // Assert
            AssertIntArray(newIntArray);
        }

        private static void AssertIntArray(int[] intArray)
        {
            ClassicAssert.IsNotNull(intArray);

            ClassicAssert.AreEqual(5, intArray.Length);

            ClassicAssert.AreEqual(100, intArray[0]);
            ClassicAssert.AreEqual(500, intArray[1]);
            ClassicAssert.AreEqual(300, intArray[2]);
            ClassicAssert.AreEqual(200, intArray[3]);
            ClassicAssert.AreEqual(400, intArray[4]);
        }

        [Test]
        public void DeserializeClassArray()
        {
            // Act
            ColorValue[] colors = JsonSerializer.ReadObject<ColorValue[]>(Properties.Resources.class_array_json);

            // Assert
            AssertClassArray(colors);
        }

        [Test]
        public void SerializeClassArray()
        {
            // Arrange
            ColorValue[] colors = JsonSerializer.ReadObject<ColorValue[]>(Properties.Resources.class_array_json);

            // Act
            string json = JsonSerializer.SerializeObject(colors);
            ColorValue[] newColors = JsonSerializer.ReadObject<ColorValue[]>(json);

            // Assert
            AssertClassArray(newColors);
        }

        private static void AssertClassArray(ColorValue[] colors)
        {
            ClassicAssert.IsNotNull(colors);

            ClassicAssert.AreEqual(7, colors.Length);

            ClassicAssert.AreEqual("red", colors[0].Color);
            ClassicAssert.AreEqual("#f00", colors[0].Value);
            ClassicAssert.AreEqual("green", colors[1].Color);
            ClassicAssert.AreEqual("#0f0", colors[1].Value);
            ClassicAssert.AreEqual("blue", colors[2].Color);
            ClassicAssert.AreEqual("#00f", colors[2].Value);
            ClassicAssert.AreEqual("cyan", colors[3].Color);
            ClassicAssert.AreEqual("#0ff", colors[3].Value);
            ClassicAssert.AreEqual("magenta", colors[4].Color);
            ClassicAssert.AreEqual("#f0f", colors[4].Value);
            ClassicAssert.AreEqual("yellow", colors[5].Color);
            ClassicAssert.AreEqual("#ff0", colors[5].Value);
            ClassicAssert.AreEqual("black", colors[6].Color);
            ClassicAssert.AreEqual("#000", colors[6].Value);
        }

        [Test]
        public void DeserializePerson()
        {
            // Act
            Person person = JsonSerializer.ReadObject<Person>(Properties.Resources.person_json);

            // Assert
            AssertPerson(person);
        }

        [Test]
        public void SerializePerson()
        {
            // Arrange
            Person person = new Person();
            person.FirstName = "John";
            person.LastName = "Smith";
            person.Age = 25;
            person.Address = new Address { StreetAddress = "21 2nd Street", City = "New York", State = "NY", PostalCode = "10021" };
            person.PhoneNumber = new[] {
                new PhoneNumber { Type = "home", Number = "212 555-1234" },
                new PhoneNumber { Type = "fax", Number = "646 555-4567" },
            };

            // Act
            string json = JsonSerializer.SerializeObject(person);
            Person deserializedPerson = JsonSerializer.ReadObject<Person>(json);

            // Assert
            AssertPerson(person);
            AssertPerson(deserializedPerson);
        }

        private static void AssertPerson(Person person)
        {
            ClassicAssert.IsNotNull(person);
            ClassicAssert.IsNotNull(person.PhoneNumber);
            ClassicAssert.IsNotNull(person.Address);

            ClassicAssert.AreEqual("John", person.FirstName);
            ClassicAssert.AreEqual("Smith", person.LastName);
            ClassicAssert.AreEqual(25, person.Age);

            ClassicAssert.AreEqual("21 2nd Street", person.Address.StreetAddress);
            ClassicAssert.AreEqual("New York", person.Address.City);
            ClassicAssert.AreEqual("NY", person.Address.State);
            ClassicAssert.AreEqual("10021", person.Address.PostalCode);

            ClassicAssert.AreEqual(2, person.PhoneNumber.Length);

            ClassicAssert.AreEqual("home", person.PhoneNumber[0].Type);
            ClassicAssert.AreEqual("212 555-1234", person.PhoneNumber[0].Number);

            ClassicAssert.AreEqual("fax", person.PhoneNumber[1].Type);
            ClassicAssert.AreEqual("646 555-4567", person.PhoneNumber[1].Number);
        }

        [Test]
        public void EveryType_String()
        {
            // Arrange
            EveryType obj = ArrangeEveryType();

            // Act
            string json = JsonSerializer.SerializeObject(obj);
            EveryType newEveryType = JsonSerializer.ReadObject<EveryType>(json);

            // Assert
            AssertEveryType(obj, newEveryType);
        }

        [Test]
        public void EveryType_Stream()
        {
            // Arrange
            EveryType obj = ArrangeEveryType();

            // Act
            string json = JsonSerializer.SerializeObject(obj);
            EveryType newEveryType;
            using (MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                newEveryType = JsonSerializer.ReadObject<EveryType>(stream);
            }

            // Assert
            AssertEveryType(obj, newEveryType);
        }

        private static EveryType ArrangeEveryType()
        {
            // Arrange
            EveryType everyType = new EveryType
                                {
                                    String = "Hello \"World\"!",
                                    NullString = null,
                                    Boolean = true,
                                    DateTime = new DateTime(2004, 3, 2),
                                    Int16 = 2,
                                    UInt16 = UInt16.MaxValue,
                                    Int32 = -5,
                                    UInt32 = 42,
                                    Int64 = 99,
                                    UInt64 = 9234032984,
                                    Single = 0.5f,
                                    Double = 4.5d,
                                    Decimal = 9.9m,
                                    BooleanArray = new[] { true, true, false, true, false },
                                    DateTimeArray = new[] { new DateTime(2011, 6, 29), new DateTime(1776, 7, 4) },
                                    StringArray = new[] { "Hello", "\"World\"", ",testing,", null },
                                    Int16Array = new short[] { 3, 4, 5 },
                                    UInt16Array = new ushort[] { 1, 2, 9 },
                                    Int32Array = new[] { -1, -8, -7, 0, 1 },
                                    UInt32Array = new uint[] { 7, 8, 9 },
                                    Int64Array = new long[] { -1232384, 120989123, -1293813 },
                                    UInt64Array = new ulong[] { 239874, 982347, 9237489 },
                                    SingleArray = new[] { 1.1f, -2.2f, 3.3f },
                                    DoubleArray = new[] { 4.4d, -5.5d, 6.6d },
                                    DecimalArray = new[] { 0.12345678m, 123123.123123m, -129380.12313m },
                                    BooleanDictionary = new Dictionary<string,bool>(),
                                    DateTimeDictionary = new Dictionary<string,DateTime>(),
                                    StringDictionary = new Dictionary<string, string>(),
                                    Int16Dictionary = new Dictionary<string, short>(),
                                    UInt16Dictionary = new Dictionary<string, ushort>(),
                                    Int32Dictionary = new Dictionary<string, int>(),
                                    UInt32Dictionary = new Dictionary<string, uint>(),
                                    Int64Dictionary = new Dictionary<string, long>(),
                                    UInt64Dictionary = new Dictionary<string, ulong>(),
                                    SingleDictionary = new Dictionary<string, float>(),
                                    DoubleDictionary = new Dictionary<string, double>(),
                                    DecimalDictionary = new Dictionary<string, decimal>(),
                                    Enum = TestEnum.Two,
                                    EnumArray = new[] { TestEnum.One, TestEnum.Three },
                                    EnumDictionary = new Dictionary<string, TestEnum>(),
                                    Class = new TestClass { Name = "foo" },
                                    ClassArray = new[] { new TestClass { Name = "bar" }, new TestClass { Name = "2000" } },
                                    ClassDictionary = new Dictionary<string, TestClass>()
                                };

            everyType.BooleanDictionary.Add("isPassing", true);
            everyType.BooleanDictionary.Add("isFailing", false);

            everyType.DateTimeDictionary.Add("anniversary", new DateTime(2008, 11, 1));
            everyType.DateTimeDictionary.Add("herBirthday", new DateTime(1980, 2, 6));

            everyType.StringDictionary.Add("one", "hello");
            everyType.StringDictionary.Add("two", "wo\\\"rld]");

            everyType.Int16Dictionary.Add("three", -3);
            everyType.Int16Dictionary.Add("four", 4);

            everyType.UInt16Dictionary.Add("five", 5123);
            everyType.UInt16Dictionary.Add("six", 13213);

            everyType.Int32Dictionary.Add("one", -123);
            everyType.Int32Dictionary.Add("\"", 1234);

            everyType.UInt32Dictionary.Add("asdf", 4444445);
            everyType.UInt32Dictionary.Add("\\", 555);

            everyType.Int64Dictionary.Add("'", 109823424);
            everyType.Int64Dictionary.Add("\"\"", 8972234);

            everyType.UInt64Dictionary.Add("one", 982374987234);
            everyType.UInt64Dictionary.Add("two", 823982748234);

            everyType.SingleDictionary.Add("uno", 0.543f);
            everyType.SingleDictionary.Add("dos", -0.599f);

            everyType.DoubleDictionary.Add("three", 9.999d);
            everyType.DoubleDictionary.Add("four", -99.999d);

            everyType.DecimalDictionary.Add("five", 1231313.12313123m);
            everyType.DecimalDictionary.Add("six", -2908234.23847924m);

            everyType.EnumDictionary.Add("[]", TestEnum.Three);
            everyType.EnumDictionary.Add("][", TestEnum.One);
            everyType.EnumDictionary.Add("abc", TestEnum.Two);
            everyType.EnumDictionary.Add("xyz", TestEnum.One);

            everyType.ClassDictionary.Add("class1", new TestClass { Name = "name 1" });
            everyType.ClassDictionary.Add("class2", new TestClass { Name = "{}[]\"\\" });

            return everyType;
        }

        private static void AssertEveryType(EveryType oldEveryType, EveryType newEveryType)
        {
            // Assert
            ClassicAssert.IsNotNull(newEveryType);

            ClassicAssert.IsNotNull(newEveryType.StringArray);
            ClassicAssert.IsNotNull(newEveryType.Int16Array);
            ClassicAssert.IsNotNull(newEveryType.UInt16Array);
            ClassicAssert.IsNotNull(newEveryType.Int32Array);
            ClassicAssert.IsNotNull(newEveryType.UInt32Array);
            ClassicAssert.IsNotNull(newEveryType.Int64Array);
            ClassicAssert.IsNotNull(newEveryType.UInt64Array);
            ClassicAssert.IsNotNull(newEveryType.SingleArray);
            ClassicAssert.IsNotNull(newEveryType.DoubleArray);
            ClassicAssert.IsNotNull(newEveryType.DecimalArray);
            ClassicAssert.IsNotNull(newEveryType.StringDictionary);
            ClassicAssert.IsNotNull(newEveryType.Int16Dictionary);
            ClassicAssert.IsNotNull(newEveryType.UInt16Dictionary);
            ClassicAssert.IsNotNull(newEveryType.Int32Dictionary);
            ClassicAssert.IsNotNull(newEveryType.UInt32Dictionary);
            ClassicAssert.IsNotNull(newEveryType.Int64Dictionary);
            ClassicAssert.IsNotNull(newEveryType.UInt64Dictionary);
            ClassicAssert.IsNotNull(newEveryType.SingleDictionary);
            ClassicAssert.IsNotNull(newEveryType.DoubleDictionary);
            ClassicAssert.IsNotNull(newEveryType.DecimalDictionary);
            ClassicAssert.IsNotNull(newEveryType.EnumArray);
            ClassicAssert.IsNotNull(newEveryType.EnumDictionary);
            ClassicAssert.IsNotNull(newEveryType.Class);
            ClassicAssert.IsNotNull(newEveryType.ClassArray);
            ClassicAssert.IsNotNull(newEveryType.ClassDictionary);

            ClassicAssert.AreEqual(oldEveryType.String, newEveryType.String);
            ClassicAssert.IsNull(newEveryType.NullString);
            ClassicAssert.AreEqual(oldEveryType.Boolean, newEveryType.Boolean);
            ClassicAssert.AreEqual(oldEveryType.DateTime, newEveryType.DateTime);
            ClassicAssert.AreEqual(oldEveryType.Int16, newEveryType.Int16);
            ClassicAssert.AreEqual(oldEveryType.UInt16, newEveryType.UInt16);
            ClassicAssert.AreEqual(oldEveryType.Int32, newEveryType.Int32);
            ClassicAssert.AreEqual(oldEveryType.UInt32, newEveryType.UInt32);
            ClassicAssert.AreEqual(oldEveryType.Int64, newEveryType.Int64);
            ClassicAssert.AreEqual(oldEveryType.UInt64, newEveryType.UInt64);
            ClassicAssert.AreEqual(oldEveryType.Single, newEveryType.Single);
            ClassicAssert.AreEqual(oldEveryType.Double, newEveryType.Double);
            ClassicAssert.AreEqual(oldEveryType.Decimal, newEveryType.Decimal);
            ClassicAssert.AreEqual(oldEveryType.Enum, newEveryType.Enum);

            ClassicAssert.AreEqual(oldEveryType.Class.Name, newEveryType.Class.Name);

            ClassicAssert.AreEqual(oldEveryType.BooleanDictionary.Count, newEveryType.BooleanDictionary.Count);
            foreach (var kvp in oldEveryType.BooleanDictionary)
            {
                ClassicAssert.AreEqual(kvp.Value, newEveryType.BooleanDictionary[kvp.Key]);
            }

            ClassicAssert.AreEqual(oldEveryType.DateTimeDictionary.Count, newEveryType.DateTimeDictionary.Count);
            foreach (var kvp in oldEveryType.DateTimeDictionary)
            {
                ClassicAssert.AreEqual(kvp.Value, newEveryType.DateTimeDictionary[kvp.Key]);
            }

            ClassicAssert.AreEqual(oldEveryType.StringDictionary.Count, newEveryType.StringDictionary.Count);
            foreach (var kvp in oldEveryType.StringDictionary)
            {
                ClassicAssert.AreEqual(kvp.Value, newEveryType.StringDictionary[kvp.Key]);
            }

            ClassicAssert.AreEqual(oldEveryType.Int16Dictionary.Count, newEveryType.Int16Dictionary.Count);
            foreach (var kvp in oldEveryType.Int16Dictionary)
            {
                ClassicAssert.AreEqual(kvp.Value, newEveryType.Int16Dictionary[kvp.Key]);
            }

            ClassicAssert.AreEqual(oldEveryType.UInt16Dictionary.Count, newEveryType.UInt16Dictionary.Count);
            foreach (var kvp in oldEveryType.UInt16Dictionary)
            {
                ClassicAssert.AreEqual(kvp.Value, newEveryType.UInt16Dictionary[kvp.Key]);
            }

            ClassicAssert.AreEqual(oldEveryType.Int32Dictionary.Count, newEveryType.Int32Dictionary.Count);
            foreach (var kvp in oldEveryType.Int32Dictionary)
            {
                ClassicAssert.AreEqual(kvp.Value, newEveryType.Int32Dictionary[kvp.Key]);
            }

            ClassicAssert.AreEqual(oldEveryType.UInt32Dictionary.Count, newEveryType.UInt32Dictionary.Count);
            foreach (var kvp in oldEveryType.UInt32Dictionary)
            {
                ClassicAssert.AreEqual(kvp.Value, newEveryType.UInt32Dictionary[kvp.Key]);
            }

            ClassicAssert.AreEqual(oldEveryType.Int64Dictionary.Count, newEveryType.Int64Dictionary.Count);
            foreach (var kvp in oldEveryType.Int64Dictionary)
            {
                ClassicAssert.AreEqual(kvp.Value, newEveryType.Int64Dictionary[kvp.Key]);
            }

            ClassicAssert.AreEqual(oldEveryType.UInt64Dictionary.Count, newEveryType.UInt64Dictionary.Count);
            foreach (var kvp in oldEveryType.UInt64Dictionary)
            {
                ClassicAssert.AreEqual(kvp.Value, newEveryType.UInt64Dictionary[kvp.Key]);
            }

            ClassicAssert.AreEqual(oldEveryType.SingleDictionary.Count, newEveryType.SingleDictionary.Count);
            foreach (var kvp in oldEveryType.SingleDictionary)
            {
                ClassicAssert.AreEqual(kvp.Value, newEveryType.SingleDictionary[kvp.Key]);
            }

            ClassicAssert.AreEqual(oldEveryType.DoubleDictionary.Count, newEveryType.DoubleDictionary.Count);
            foreach (var kvp in oldEveryType.DoubleDictionary)
            {
                ClassicAssert.AreEqual(kvp.Value, newEveryType.DoubleDictionary[kvp.Key]);
            }

            ClassicAssert.AreEqual(oldEveryType.DecimalDictionary.Count, newEveryType.DecimalDictionary.Count);
            foreach (var kvp in oldEveryType.DecimalDictionary)
            {
                ClassicAssert.AreEqual(kvp.Value, newEveryType.DecimalDictionary[kvp.Key]);
            }

            ClassicAssert.AreEqual(oldEveryType.EnumDictionary.Count, newEveryType.EnumDictionary.Count);
            foreach (var kvp in oldEveryType.EnumDictionary)
            {
                ClassicAssert.AreEqual(kvp.Value, newEveryType.EnumDictionary[kvp.Key]);
            }

            ClassicAssert.AreEqual(oldEveryType.ClassDictionary.Count, newEveryType.ClassDictionary.Count);
            foreach (var kvp in oldEveryType.ClassDictionary)
            {
                ClassicAssert.AreEqual(kvp.Value.Name, newEveryType.ClassDictionary[kvp.Key].Name);
            }

            ClassicAssert.AreEqual(oldEveryType.BooleanArray.Length, newEveryType.BooleanArray.Length);
            for (int i = 0; i < oldEveryType.BooleanArray.Length; i++)
                ClassicAssert.AreEqual(oldEveryType.BooleanArray[i], newEveryType.BooleanArray[i]);

            ClassicAssert.AreEqual(oldEveryType.DateTimeArray.Length, newEveryType.DateTimeArray.Length);
            for (int i = 0; i < oldEveryType.DateTimeArray.Length; i++)
                ClassicAssert.AreEqual(oldEveryType.DateTimeArray[i], newEveryType.DateTimeArray[i]);

            ClassicAssert.AreEqual(oldEveryType.StringArray.Length, newEveryType.StringArray.Length);
            for (int i = 0; i < oldEveryType.StringArray.Length; i++)
                ClassicAssert.AreEqual(oldEveryType.StringArray[i], newEveryType.StringArray[i]);

            ClassicAssert.AreEqual(oldEveryType.Int16Array.Length, newEveryType.Int16Array.Length);
            for (int i = 0; i < oldEveryType.Int16Array.Length; i++)
                ClassicAssert.AreEqual(oldEveryType.Int16Array[i], newEveryType.Int16Array[i]);

            ClassicAssert.AreEqual(oldEveryType.UInt16Array.Length, newEveryType.UInt16Array.Length);
            for (int i = 0; i < oldEveryType.UInt16Array.Length; i++)
                ClassicAssert.AreEqual(oldEveryType.UInt16Array[i], newEveryType.UInt16Array[i]);

            ClassicAssert.AreEqual(oldEveryType.Int32Array.Length, newEveryType.Int32Array.Length);
            for (int i = 0; i < oldEveryType.Int32Array.Length; i++)
                ClassicAssert.AreEqual(oldEveryType.Int32Array[i], newEveryType.Int32Array[i]);

            ClassicAssert.AreEqual(oldEveryType.UInt32Array.Length, newEveryType.UInt32Array.Length);
            for (int i = 0; i < oldEveryType.UInt32Array.Length; i++)
                ClassicAssert.AreEqual(oldEveryType.UInt32Array[i], newEveryType.UInt32Array[i]);

            ClassicAssert.AreEqual(oldEveryType.Int64Array.Length, newEveryType.Int64Array.Length);
            for (int i = 0; i < oldEveryType.Int64Array.Length; i++)
                ClassicAssert.AreEqual(oldEveryType.Int64Array[i], newEveryType.Int64Array[i]);

            ClassicAssert.AreEqual(oldEveryType.UInt64Array.Length, newEveryType.UInt64Array.Length);
            for (int i = 0; i < oldEveryType.UInt64Array.Length; i++)
                ClassicAssert.AreEqual(oldEveryType.UInt64Array[i], newEveryType.UInt64Array[i]);

            ClassicAssert.AreEqual(oldEveryType.SingleArray.Length, newEveryType.SingleArray.Length);
            for (int i = 0; i < oldEveryType.SingleArray.Length; i++)
                ClassicAssert.AreEqual(oldEveryType.SingleArray[i], newEveryType.SingleArray[i]);

            ClassicAssert.AreEqual(oldEveryType.DoubleArray.Length, newEveryType.DoubleArray.Length);
            for (int i = 0; i < oldEveryType.DoubleArray.Length; i++)
                ClassicAssert.AreEqual(oldEveryType.DoubleArray[i], newEveryType.DoubleArray[i]);

            ClassicAssert.AreEqual(oldEveryType.DecimalArray.Length, newEveryType.DecimalArray.Length);
            for (int i = 0; i < oldEveryType.DecimalArray.Length; i++)
                ClassicAssert.AreEqual(oldEveryType.DecimalArray[i], newEveryType.DecimalArray[i]);

            ClassicAssert.AreEqual(oldEveryType.EnumArray.Length, newEveryType.EnumArray.Length);
            for (int i = 0; i < oldEveryType.EnumArray.Length; i++)
                ClassicAssert.AreEqual(oldEveryType.EnumArray[i], newEveryType.EnumArray[i]);

            ClassicAssert.AreEqual(oldEveryType.ClassArray.Length, newEveryType.ClassArray.Length);
            for (int i = 0; i < oldEveryType.ClassArray.Length; i++)
                ClassicAssert.AreEqual(oldEveryType.ClassArray[i].Name, newEveryType.ClassArray[i].Name);
        }
    }
}
