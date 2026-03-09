using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Collections;
using AgentX.Tests.Helpers;
using FluentAssertions;
using Moq;
using Serilog;
using Xunit;

namespace AgentX.Tests.Services;

/// <summary>
/// Unit tests for <see cref="CollectionService"/>.
/// Uses an in-memory SQLite database via <see cref="TestDbContextFactory"/>
/// and a Moq-based <see cref="ILogger"/> for all logging dependencies.
/// </summary>
public sealed class CollectionServiceTests : IDisposable
{
    private readonly TestDbContextFactory _factory;
    private readonly Mock<ILogger> _loggerMock;

    public CollectionServiceTests()
    {
        _factory = new TestDbContextFactory();
        _loggerMock = new Mock<ILogger>();

        // Serilog's ForContext<T>() returns a new ILogger; mock it to return itself.
        _loggerMock
            .Setup(l => l.ForContext<CollectionService>())
            .Returns(_loggerMock.Object);
    }

    public void Dispose() => _factory.Dispose();

    /// <summary>
    /// Creates a <see cref="CollectionService"/> backed by a fresh DbContext
    /// from the shared in-memory database.
    /// </summary>
    private CollectionService CreateService()
    {
        var db = _factory.CreateContext();
        return new CollectionService(db, _loggerMock.Object);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  CreateCollectionAsync
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CreateCollectionAsync_WithValidName_CreatesCollectionWithCorrectName()
    {
        // Arrange
        var sut = CreateService();

        // Act
        var collection = await sut.CreateCollectionAsync("Research Papers");

        // Assert
        collection.Should().NotBeNull();
        collection.Name.Should().Be("Research Papers");
        collection.Id.Should().BeGreaterThan(0);
        collection.ParentCollectionId.Should().BeNull();
        collection.DocumentCount.Should().Be(0);
        collection.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        collection.UpdatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task CreateCollectionAsync_TrimsNameAndDescription()
    {
        // Arrange
        var sut = CreateService();

        // Act
        var collection = await sut.CreateCollectionAsync("  My Collection  ", "  A description  ");

        // Assert
        collection.Name.Should().Be("My Collection");
        collection.Description.Should().Be("A description");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateCollectionAsync_WithNullOrEmptyName_ThrowsArgumentException(string? name)
    {
        // Arrange
        var sut = CreateService();

        // Act
        var act = () => sut.CreateCollectionAsync(name!);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("name");
    }

    [Fact]
    public async Task CreateCollectionAsync_WithValidParent_SetsCorrectParentCollectionId()
    {
        // Arrange
        var sut = CreateService();
        var parent = await sut.CreateCollectionAsync("Parent");

        // Act
        var child = await sut.CreateCollectionAsync("Child", parentId: parent.Id);

        // Assert
        child.ParentCollectionId.Should().Be(parent.Id);
    }

    [Fact]
    public async Task CreateCollectionAsync_WithNonExistentParent_ThrowsInvalidOperationException()
    {
        // Arrange
        var sut = CreateService();

        // Act
        var act = () => sut.CreateCollectionAsync("Orphan", parentId: 99999);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not found*");
    }

    [Fact]
    public async Task CreateCollectionAsync_AssignsSortOrderSequentially()
    {
        // Arrange
        var sut = CreateService();

        // Act
        var first = await sut.CreateCollectionAsync("First");
        var second = await sut.CreateCollectionAsync("Second");
        var third = await sut.CreateCollectionAsync("Third");

        // Assert
        first.SortOrder.Should().Be(0);
        second.SortOrder.Should().Be(1);
        third.SortOrder.Should().Be(2);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  GetAllCollectionsAsync
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetAllCollectionsAsync_WhenEmpty_ReturnsEmptyList()
    {
        // Arrange
        var sut = CreateService();

        // Act
        var collections = await sut.GetAllCollectionsAsync();

        // Assert
        collections.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllCollectionsAsync_ReturnsCreatedCollections()
    {
        // Arrange
        var sut = CreateService();
        await sut.CreateCollectionAsync("Alpha");
        await sut.CreateCollectionAsync("Beta");
        await sut.CreateCollectionAsync("Gamma");

        // Act (use a fresh service to verify persistence in the same DB)
        var reader = CreateService();
        var collections = await reader.GetAllCollectionsAsync();

        // Assert
        collections.Should().HaveCount(3);
        collections.Select(c => c.Name).Should().Contain(new[] { "Alpha", "Beta", "Gamma" });
    }

    // ══════════════════════════════════════════════════════════════════════
    //  GetRootCollectionsAsync
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetRootCollectionsAsync_OnlyReturnsRootLevelCollections()
    {
        // Arrange
        var sut = CreateService();
        var root1 = await sut.CreateCollectionAsync("Root 1");
        var root2 = await sut.CreateCollectionAsync("Root 2");
        await sut.CreateCollectionAsync("Child of Root 1", parentId: root1.Id);
        await sut.CreateCollectionAsync("Child of Root 2", parentId: root2.Id);

        // Act
        var reader = CreateService();
        var rootCollections = await reader.GetRootCollectionsAsync();

        // Assert
        rootCollections.Should().HaveCount(2);
        rootCollections.Select(c => c.Name)
            .Should().BeEquivalentTo(new[] { "Root 1", "Root 2" });
        rootCollections.Should().OnlyContain(c => c.ParentCollectionId == null);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  UpdateCollectionAsync
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task UpdateCollectionAsync_UpdatesNameAndDescription()
    {
        // Arrange
        var sut = CreateService();
        var collection = await sut.CreateCollectionAsync("Original", "Old description");

        // Act
        await sut.UpdateCollectionAsync(collection.Id, "Updated", "New description");

        // Assert
        var reader = CreateService();
        var updated = await reader.GetCollectionAsync(collection.Id);
        updated.Should().NotBeNull();
        updated!.Name.Should().Be("Updated");
        updated.Description.Should().Be("New description");
        updated.UpdatedAt.Should().BeAfter(collection.CreatedAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task UpdateCollectionAsync_WithEmptyName_ThrowsArgumentException(string name)
    {
        // Arrange
        var sut = CreateService();
        var collection = await sut.CreateCollectionAsync("Valid Name");

        // Act
        var act = () => sut.UpdateCollectionAsync(collection.Id, name);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("name");
    }

    [Fact]
    public async Task UpdateCollectionAsync_WithNonExistentId_ThrowsInvalidOperationException()
    {
        // Arrange
        var sut = CreateService();

        // Act
        var act = () => sut.UpdateCollectionAsync(99999, "Name");

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not found*");
    }

    // ══════════════════════════════════════════════════════════════════════
    //  DeleteCollectionAsync
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task DeleteCollectionAsync_RemovesCollection()
    {
        // Arrange
        var sut = CreateService();
        var collection = await sut.CreateCollectionAsync("To Delete");
        var id = collection.Id;

        // Act
        await sut.DeleteCollectionAsync(id);

        // Assert
        var reader = CreateService();
        var deleted = await reader.GetCollectionAsync(id);
        deleted.Should().BeNull();
    }

    [Fact]
    public async Task DeleteCollectionAsync_ReparentsChildrenToDeletedCollectionsParent()
    {
        // Arrange
        var sut = CreateService();
        var grandparent = await sut.CreateCollectionAsync("Grandparent");
        var parent = await sut.CreateCollectionAsync("Parent", parentId: grandparent.Id);
        var child = await sut.CreateCollectionAsync("Child", parentId: parent.Id);

        // Act: delete the middle collection (Parent)
        await sut.DeleteCollectionAsync(parent.Id);

        // Assert: Child should now be parented under Grandparent
        var reader = CreateService();
        var reparentedChild = await reader.GetCollectionAsync(child.Id);
        reparentedChild.Should().NotBeNull();
        reparentedChild!.ParentCollectionId.Should().Be(grandparent.Id);
    }

    [Fact]
    public async Task DeleteCollectionAsync_NonExistentId_DoesNotThrow()
    {
        // Arrange
        var sut = CreateService();

        // Act
        var act = () => sut.DeleteCollectionAsync(99999);

        // Assert: the implementation silently returns for missing collections
        await act.Should().NotThrowAsync();
    }

    // ══════════════════════════════════════════════════════════════════════
    //  MoveCollectionAsync — circular reference and self-parenting guards
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task MoveCollectionAsync_PreventsCircularReference()
    {
        // Arrange: A -> B -> C; then try to move A under C
        var sut = CreateService();
        var a = await sut.CreateCollectionAsync("A");
        var b = await sut.CreateCollectionAsync("B", parentId: a.Id);
        var c = await sut.CreateCollectionAsync("C", parentId: b.Id);

        // Act: moving A under C would create C -> A -> B -> C (circular)
        var act = () => sut.MoveCollectionAsync(a.Id, c.Id);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*circular*");
    }

    [Fact]
    public async Task MoveCollectionAsync_PreventsSelfParenting()
    {
        // Arrange
        var sut = CreateService();
        var collection = await sut.CreateCollectionAsync("Self");

        // Act
        var act = () => sut.MoveCollectionAsync(collection.Id, collection.Id);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*itself*");
    }

    [Fact]
    public async Task MoveCollectionAsync_ValidMove_UpdatesParent()
    {
        // Arrange
        var sut = CreateService();
        var source = await sut.CreateCollectionAsync("Source");
        var newParent = await sut.CreateCollectionAsync("New Parent");

        // Act
        await sut.MoveCollectionAsync(source.Id, newParent.Id);

        // Assert
        var reader = CreateService();
        var moved = await reader.GetCollectionAsync(source.Id);
        moved.Should().NotBeNull();
        moved!.ParentCollectionId.Should().Be(newParent.Id);
    }

    [Fact]
    public async Task MoveCollectionAsync_MoveToRoot_SetsParentToNull()
    {
        // Arrange
        var sut = CreateService();
        var parent = await sut.CreateCollectionAsync("Parent");
        var child = await sut.CreateCollectionAsync("Child", parentId: parent.Id);

        // Act
        await sut.MoveCollectionAsync(child.Id, null);

        // Assert
        var reader = CreateService();
        var moved = await reader.GetCollectionAsync(child.Id);
        moved.Should().NotBeNull();
        moved!.ParentCollectionId.Should().BeNull();
    }

    // ══════════════════════════════════════════════════════════════════════
    //  AddDocumentToCollectionAsync
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task AddDocumentToCollectionAsync_CreatesAssociation()
    {
        // Arrange
        var db = _factory.CreateContext();
        var sut = new CollectionService(db, _loggerMock.Object);

        var collection = await sut.CreateCollectionAsync("Docs");

        // Create a document directly in the DB (CollectionService doesn't manage documents)
        var doc = new DocumentEntity
        {
            FileName = "test.pdf",
            FilePath = "/tmp/test.pdf",
            FileType = "pdf",
            ContentHash = "abc123",
            ImportedAt = DateTime.UtcNow,
            FileModifiedAt = DateTime.UtcNow,
            IndexingStatus = "completed"
        };
        db.Documents.Add(doc);
        await db.SaveChangesAsync();

        // Act
        await sut.AddDocumentToCollectionAsync(doc.Id, collection.Id);

        // Assert
        var reader = CreateService();
        var updatedCollection = await reader.GetCollectionAsync(collection.Id);
        updatedCollection.Should().NotBeNull();
        updatedCollection!.DocumentCount.Should().Be(1);
        updatedCollection.DocumentCollections.Should().ContainSingle(dc => dc.DocumentId == doc.Id);
    }

    [Fact]
    public async Task AddDocumentToCollectionAsync_DuplicateAssociation_DoesNotCreateDuplicate()
    {
        // Arrange
        var db = _factory.CreateContext();
        var sut = new CollectionService(db, _loggerMock.Object);

        var collection = await sut.CreateCollectionAsync("Docs");

        var doc = new DocumentEntity
        {
            FileName = "test.pdf",
            FilePath = "/tmp/test.pdf",
            FileType = "pdf",
            ContentHash = "abc123",
            ImportedAt = DateTime.UtcNow,
            FileModifiedAt = DateTime.UtcNow,
            IndexingStatus = "completed"
        };
        db.Documents.Add(doc);
        await db.SaveChangesAsync();

        // Act: add twice
        await sut.AddDocumentToCollectionAsync(doc.Id, collection.Id);
        await sut.AddDocumentToCollectionAsync(doc.Id, collection.Id);

        // Assert: should still only have one association
        var reader = CreateService();
        var updatedCollection = await reader.GetCollectionAsync(collection.Id);
        updatedCollection.Should().NotBeNull();
        updatedCollection!.DocumentCount.Should().Be(1);
    }

    [Fact]
    public async Task AddDocumentToCollectionAsync_NonExistentDocument_ThrowsInvalidOperationException()
    {
        // Arrange
        var sut = CreateService();
        var collection = await sut.CreateCollectionAsync("Docs");

        // Act
        var act = () => sut.AddDocumentToCollectionAsync(99999, collection.Id);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Document*not found*");
    }

    [Fact]
    public async Task AddDocumentToCollectionAsync_NonExistentCollection_ThrowsInvalidOperationException()
    {
        // Arrange
        var db = _factory.CreateContext();
        var sut = new CollectionService(db, _loggerMock.Object);

        var doc = new DocumentEntity
        {
            FileName = "test.pdf",
            FilePath = "/tmp/test.pdf",
            FileType = "pdf",
            ContentHash = "abc123",
            ImportedAt = DateTime.UtcNow,
            FileModifiedAt = DateTime.UtcNow,
            IndexingStatus = "completed"
        };
        db.Documents.Add(doc);
        await db.SaveChangesAsync();

        // Act
        var act = () => sut.AddDocumentToCollectionAsync(doc.Id, 99999);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Collection*not found*");
    }

    // ══════════════════════════════════════════════════════════════════════
    //  GetCollectionCountAsync
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetCollectionCountAsync_WhenEmpty_ReturnsZero()
    {
        // Arrange
        var sut = CreateService();

        // Act
        var count = await sut.GetCollectionCountAsync();

        // Assert
        count.Should().Be(0);
    }

    [Fact]
    public async Task GetCollectionCountAsync_ReturnsCorrectCount()
    {
        // Arrange
        var sut = CreateService();
        await sut.CreateCollectionAsync("One");
        await sut.CreateCollectionAsync("Two");
        await sut.CreateCollectionAsync("Three");

        // Act
        var reader = CreateService();
        var count = await reader.GetCollectionCountAsync();

        // Assert
        count.Should().Be(3);
    }

    [Fact]
    public async Task GetCollectionCountAsync_IncludesChildCollections()
    {
        // Arrange
        var sut = CreateService();
        var root = await sut.CreateCollectionAsync("Root");
        await sut.CreateCollectionAsync("Child 1", parentId: root.Id);
        await sut.CreateCollectionAsync("Child 2", parentId: root.Id);

        // Act
        var reader = CreateService();
        var count = await reader.GetCollectionCountAsync();

        // Assert: root + 2 children = 3
        count.Should().Be(3);
    }
}
